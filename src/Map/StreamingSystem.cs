using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Streams scene entities in and out as the editor's focus moves. Each update it scans the current
/// map's storages for entities whose bounds overlap a box around the focus on a background thread,
/// then on the main thread reconciles the scene registry: newly in-range entities are added, and every
/// entity in the registry is re-judged by <see cref="FateOf"/>. Runs one scan at a time and only
/// re-scans once the focus has moved far enough (or the map changed).
///
/// Entering another map is just a scan whose results share nothing with the last one, so the previous
/// map's entities unload — except the ones the edit session pins, which stay in memory (but out of
/// sight) until the session is committed or aborted.
///
/// Streaming judges every non-resident entity in the registry, not just the ones it put there itself.
/// Tracking only what it loaded, keyed by row id, would miss an entity created in the editor (it has no
/// row yet), which would stay drawn and listed over a chunk that was long gone. Judging the registry has
/// no such gap, so a new entity and a modified one behave identically.
///
/// Two regions, not one. The <b>view</b> is what the user is looking at and what loaders produce for.
/// The <b>load</b> region is wider, by whatever margin a loader declares, and is what stored entities
/// are read over: derived content at the edge of view is only correct if the entities just past that
/// edge are loaded. Entities that fall in the margin but not the view are marked peripheral — loaded
/// as inputs, but not drawn, listed or picked.
/// </summary>
public sealed class StreamingSystem : IWorldParticipant
{
    // Used when no landscape is loaded to give the chunk count a size regardless.
    private const float DefaultChunkWorldSize = 64.0f;

    // Height is not distance. Terrain is addressed on the ground plane and a map is authored from
    // above, so climbing must not unload the world underneath you — which a cubic box would do.
    private const float VerticalRange = 4096.0f;

    private readonly EditorContext _context;
    private readonly List<ISceneEntityLoader> _loaders = [];

    // The live entities the last applied scan reported, which is not the same as "everything loaded":
    // an entity created in the editor is loaded but has no row for a scan to find.
    private readonly HashSet<SceneEntity> _present = [];

    private Task<(List<SceneEntity> Built, List<(Type Type, long Key)> Seen, List<CatalogEntity> Catalog)>? _pendingScan;
    private MapId _scanMap;
    private Aabb _scanView;
    private Vector3 _lastFocus;
    private bool _scanStarted;  // a scan has been launched for the current map and position
    private bool _reconciled;   // a scan has landed, so there is a region to judge fates against
    private string _invalidatedBy = string.Empty;
    private long _scanClock;

    public StreamingSystem(EditorContext context)
    {
        _context = context;
    }

    // The open map's chunk world size, or a default when no landscape is loaded.
    private float ChunkWorldSize() =>
        _context.Landscape.Settings?.ChunkWorldSize is > 0.0f and float size ? size : DefaultChunkWorldSize;

    // Focus travel before a re-scan — about one chunk. A rescan only builds terrain batches that are
    // not already loaded and only adds stored entities not already present, so re-scanning often and
    // small keeps terrain close behind the camera; scaling this up with the view distance just makes
    // terrain lag further behind while flying.
    private float RescanDistance => ChunkWorldSize();

    /// <summary>What streaming does with one loaded entity when it re-judges the registry.</summary>
    public enum EntityFate
    {
        /// <summary>In the open map and in view: drawn, listed, picked and selectable.</summary>
        Visible,

        /// <summary>Held in memory but out of reach: not drawn, listed, picked or selectable.</summary>
        Hidden,

        /// <summary>Nothing is holding it any more, so it leaves the scene entirely.</summary>
        Unload,
    }

    /// <summary>
    /// Registers a source of scene entities that has no storage behind it. Used by the landscape to
    /// stream chunks, which are generated from the grid rather than read from a table.
    /// </summary>
    public void AddLoader(ISceneEntityLoader loader) => _loaders.Add(loader);

    /// <summary>Whether a scan is in flight — a reload's quiescence wait gates on this so an unload
    /// never runs while a background scan is about to write results into what it just dropped.</summary>
    bool IWorldParticipant.IsBusy => _pendingScan != null;

    /// <summary>
    /// Observes and applies a landed scan without starting a new one. <see cref="Update"/> normally
    /// does this as a side effect of its own per-frame call, but that only runs while the editor scene
    /// is active — a <see cref="WorldReload"/> quiescing on <see cref="IWorldParticipant.IsBusy"/>
    /// needs to keep draining this itself, or a scan that was in flight the instant the reload started
    /// would sit "busy" forever: nothing else would ever notice it finished.
    /// </summary>
    public void PumpCompletion() => ApplyCompletedScan();

    /// <summary>
    /// Forgets everything read from the database: the last landed scan, what is present, and the
    /// pending one if there was one — quiescence should already guarantee there wasn't. Does not
    /// touch <see cref="ScanVersion"/>, which stays monotonic so a version comparison elsewhere
    /// (image chunk residency) never sees it go backwards.
    /// </summary>
    void IWorldParticipant.UnloadWorld()
    {
        _pendingScan = null;
        _present.Clear();
        _scanStarted = false;
        _reconciled = false;
    }

    /// <summary>Whether any scan has landed yet. <see cref="LoadRegion"/> and <see cref="ScanMap"/> are
    /// meaningless (a default, zero-sized box; a default map) before this is true.</summary>
    public bool Reconciled => _reconciled;

    /// <summary>Bumped every time a scan lands — what a system whose own "worth having resident"
    /// targets follow this same region (image chunk residency) gates its recompute on, instead of
    /// walking the scene every frame regardless of whether anything changed.</summary>
    public int ScanVersion { get; private set; }

    /// <summary>The map the most recently landed scan covered.</summary>
    public MapId ScanMap => _scanMap;

    /// <summary>The load region — the view grown by every loader's declared margin — the most recently
    /// landed scan covered, in world space. Stored entities are read over this wider region because
    /// they are what derived content is built from; a system that needs the same "what's worth having
    /// loaded right now" answer streaming itself uses (image chunk residency) reads this rather than
    /// recomputing its own.</summary>
    public Aabb LoadRegion => Grow(_scanView, LoadMargin());

    /// <summary>
    /// Forces the next update to re-scan, regardless of how far the focus has moved. What a loader
    /// returns can depend on more than position — landscape chunks are rebuilt from the entities that
    /// shape them — so an edit has to be able to say "what you have is stale".
    /// </summary>
    public void Invalidate([CallerMemberName] string member = "", [CallerFilePath] string file = "")
    {
        _scanStarted = false;
        _invalidatedBy = $"{Path.GetFileNameWithoutExtension(file)}.{member}";
    }

    /// <summary>
    /// Drops every derived terrain entity and forces a fresh scan. What terrain chunks are grouped
    /// into for rendering is a display choice, so changing it has to discard what is loaded rather
    /// than wait for the view to move past it.
    /// </summary>
    public void ReloadTerrain()
    {
        foreach (SceneEntity entity in new List<SceneEntity>(_context.Scene.Entities))
        {
            if (entity is not LandscapeTerrainBatch)
            {
                continue;
            }

            _context.Selection.Remove(entity);
            entity.DestroyRepresentation();
            _context.Scene.Remove(entity);
            _present.Remove(entity);
        }

        Invalidate();
    }

    /// <summary>
    /// Re-judges every loaded entity right now against the last scan, then forces a re-scan.
    /// Called when the edit session ends.
    ///
    /// Releasing the session's pins changes what is holding entities in the scene, and that has to
    /// take effect immediately: a scan is asynchronous and, since it is also gated on the focus having
    /// moved, may not run for a long time. Until it did, an entity that only stayed loaded because it
    /// was dirty went on being drawn and listed after the commit that settled it — and the user had to
    /// fly somewhere else to make the editor notice.
    /// </summary>
    public void Resweep()
    {
        Invalidate();

        // Nothing has been scanned yet, so there is no region to judge against; the first scan will
        // do it. Judging against a default region here would unload the whole scene.
        if (_reconciled)
        {
            ApplyFates();
        }
    }

    /// <summary>Called each frame with the viewport focus (camera position, in Godot space).</summary>
    public void Update(Vector3 focus)
    {
        ApplyCompletedScan();

        if (_pendingScan != null)
        {
            return;
        }

        MapId map = _context.Maps.CurrentMap;
        bool mapChanged = !_scanStarted || !map.Equals(_scanMap);

        // Horizontal only: the view region's vertical extent is already unbounded (see VerticalRange),
        // so which chunks are visible depends solely on X/Z. Comparing full 3D distance treated pure
        // vertical flight — climbing or descending, with the camera's footprint unchanged — as motion
        // that demanded a re-scan, which drove a full rebuild of the entire visible chunk set on every
        // 32 units of altitude change for no reason: nothing about what should be loaded had moved.
        if (!mapChanged && HorizontalDistance(focus, _lastFocus) < RescanDistance)
        {
            return;
        }

        // Invalidation first: it clears _scanStarted, which is half of what mapChanged tests, so
        // asking mapChanged first reports every forced re-scan as a map change.
        DiagnosticLog.Log(
            _invalidatedBy.Length > 0 ? $"start: invalidated by {_invalidatedBy}"
            : !map.Equals(_scanMap) ? "start: map changed"
            : !_scanStarted ? "start: first scan"
            : $"start: focus moved {HorizontalDistance(focus, _lastFocus):F0}");
        _invalidatedBy = string.Empty;
        _scanClock = DiagnosticLog.Start();

        _scanStarted = true;
        _scanMap = map;
        _lastFocus = focus;

        // Loaders capture live editor state here, on the main thread, before the scan can hop off it.
        foreach (ISceneEntityLoader loader in _loaders)
        {
            loader.Prepare();
        }

        // Captured here, on the main thread, for the same reason Prepare is: the scan runs off the
        // main thread and must not read the scene registry. Each factory is handed the keys of its
        // own entities that are already loaded, so it can report them without rebuilding them.
        var loadedKeys = new Dictionary<Type, HashSet<long>>();
        foreach (SceneEntity entity in _context.Scene.Entities)
        {
            if (KeyOf(entity) is not long key)
            {
                continue;
            }

            Type type = entity.GetType();
            if (!loadedKeys.TryGetValue(type, out HashSet<long>? keys))
            {
                keys = [];
                loadedKeys[type] = keys;
            }

            keys.Add(key);
        }

        // Recorded here, on the main thread, and read back when the scan lands: the view a scan was
        // taken over is what every later fate is judged against, so it must not be written from the
        // scan's own thread. Only one scan is ever in flight, so it cannot change underneath one.
        float range = _context.View.ViewDistanceChunks * ChunkWorldSize();
        var extent = new Vector3(range, VerticalRange, range);
        _scanView = new Aabb(focus - extent, extent * 2.0f);
        _pendingScan = ScanAsync(map, _scanView, Grow(_scanView, LoadMargin()), loadedKeys);
    }

    private void ApplyCompletedScan()
    {
        if (_pendingScan is not { IsCompleted: true })
        {
            return;
        }

        Task<(List<SceneEntity> Built, List<(Type Type, long Key)> Seen, List<CatalogEntity> Catalog)> scan = _pendingScan;
        _pendingScan = null;

        if (!scan.IsCompletedSuccessfully)
        {
            GD.PushError($"[Streaming] Scan failed: {scan.Exception?.GetBaseException().Message}");
            return;
        }

        double elapsed = DiagnosticLog.MillisecondsSince(_scanClock);
        long reconcileClock = DiagnosticLog.Start();
        Reconcile(scan.Result.Built, scan.Result.Seen, scan.Result.Catalog);
        DiagnosticLog.Log(
            $"done: {elapsed:F0}ms scan + {DiagnosticLog.MillisecondsSince(reconcileClock):F0}ms reconcile, "
            + $"{scan.Result.Built.Count} built of {scan.Result.Seen.Count} in region");
    }

    /// <summary>The widest margin any loader needs its inputs loaded over.</summary>
    private float LoadMargin()
    {
        float margin = 0.0f;
        foreach (ISceneEntityLoader loader in _loaders)
        {
            margin = Mathf.Max(margin, loader.LoadMargin);
        }

        return margin;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return Mathf.Sqrt((dx * dx) + (dz * dz));
    }

    // Horizontally only: the vertical extent is already effectively unbounded.
    private static Aabb Grow(Aabb region, float margin) =>
        margin <= 0.0f
            ? region
            : new Aabb(region.Position - new Vector3(margin, 0.0f, margin),
                       region.Size + new Vector3(margin * 2.0f, 0.0f, margin * 2.0f));

    // Stored entities are loaded over the wider region, because they are what derived data is built
    // from; loaders produce what the user sees, so they get the view region.
    private async Task<(List<SceneEntity> Built, List<(Type Type, long Key)> Seen, List<CatalogEntity> Catalog)> ScanAsync(
        MapId map, Aabb view, Aabb load, Dictionary<Type, HashSet<long>> loadedKeys)
    {
        using IDisposable scope = DiagnosticLog.Scope($"scan {ScanVersion + 1}");
        var built = new List<SceneEntity>();
        var seen = new List<(Type Type, long Key)>();
        var catalog = new List<CatalogEntity>();
        foreach (Storage storage in _context.Database.Storages)
        {
            // One reader for the whole storage rather than one per factory: re-acquiring per factory
            // lets an unrelated writer (image-chunk residency loads, mostly) wedge in between every
            // factory, and each of those stalls the scan by however long that write runs — turning a
            // handful of millisecond queries into seconds.
            long lockClock = DiagnosticLog.Start();
            using IDisposable read = await storage.Lock.ReaderAsync().ConfigureAwait(false);
            DiagnosticLog.Log($"  {storage.Name}: reader lock {DiagnosticLog.MillisecondsSince(lockClock):F0}ms");

            foreach (ISceneEntityFactory factory in storage.SceneFactories)
            {
                using IDisposable factoryScope = DiagnosticLog.Scope(factory.GetType().Name);
                long factoryClock = DiagnosticLog.Start();
                HashSet<long> known = loadedKeys.TryGetValue(factory.EntityType, out HashSet<long>? set) ? set : [];
                SceneEntityScan scanned = await factory.ScanAsync(map, load, known, publishing: true).ConfigureAwait(false);
                DiagnosticLog.Log(
                    $"  {factory.GetType().Name}: {DiagnosticLog.MillisecondsSince(factoryClock):F0}ms, "
                    + $"{scanned.Built.Count} built of {scanned.Keys.Count} in region");
                built.AddRange(scanned.Built);
                catalog.AddRange(scanned.Catalog);
                foreach (long key in scanned.Keys)
                {
                    seen.Add((factory.EntityType, key));
                }
            }
        }

        // Storage-free sources (landscape chunks) take no lock: there is no database behind them.
        foreach (ISceneEntityLoader loader in _loaders)
        {
            using IDisposable loaderScope = DiagnosticLog.Scope(loader.GetType().Name);
            long loaderClock = DiagnosticLog.Start();
            IReadOnlyList<SceneEntity> produced = await loader.ScanAsync(map, view).ConfigureAwait(false);
            DiagnosticLog.Log(
                $"  {loader.GetType().Name}: {DiagnosticLog.MillisecondsSince(loaderClock):F0}ms, {produced.Count} entities");
            built.AddRange(produced);
            foreach (SceneEntity entity in produced)
            {
                seen.Add((entity.GetType(), loader.KeyOf(entity)));
            }
        }

        return (built, seen, catalog);
    }

    private void Reconcile(List<SceneEntity> built, List<(Type Type, long Key)> seen, List<CatalogEntity> catalog)
    {
        // Published before Scene.Add below, so a placement is never observed with an unresolved model —
        // see ProceduralComponent.Model and SceneEntityScanCatalog.Publishing.
        foreach (CatalogEntity entity in catalog)
        {
            if (!_context.Catalog.Contains(entity))
            {
                _context.Catalog.Add(entity);
            }
        }

        // Index entities already in the scene that carry a persistent key, so a scan never duplicates
        // one that is already loaded — including one this session created and committed, whose row
        // the scan is seeing for the first time.
        var loaded = new Dictionary<(Type, long), SceneEntity>();
        foreach (SceneEntity entity in _context.Scene.Entities)
        {
            if (KeyOf(entity) is long key)
            {
                loaded[(entity.GetType(), key)] = entity;
            }
        }

        _present.Clear();
        foreach (SceneEntity entity in built)
        {
            if (KeyOf(entity) is not long key)
            {
                continue;
            }

            var id = (entity.GetType(), key);
            if (loaded.TryGetValue(id, out SceneEntity? existing))
            {
                // A re-scan of derived content is a rebuild, so its result replaces what is loaded
                // rather than being thrown away as a duplicate.
                Refresh(existing, entity);
                _present.Add(existing);
            }
            else
            {
                // The registry publish above already covers this entity's model, if it named one; the
                // attachment would only ever resurrect a stale copy from here on.
                entity.Component<ProceduralComponent>()?.ClearAttachment();
                _context.Scene.Add(entity);
                loaded[id] = entity;
                _present.Add(entity);
            }
        }

        // Entities the scan matched but did not rebuild because they were already loaded. Present is
        // what FateOf reads to keep a peripheral input resident, so a key the scan saw counts the same
        // whether an entity was constructed for it or not.
        foreach ((Type type, long key) in seen)
        {
            if (loaded.TryGetValue((type, key), out SceneEntity? existing))
            {
                _present.Add(existing);
            }
        }

        _reconciled = true;
        ScanVersion++;
        ApplyFates();
    }

    /// <summary>
    /// What becomes of one loaded entity: where it is decides whether it can be seen, and only then
    /// does what is holding it decide whether it stays in memory at all.
    ///
    /// Visibility is a question about place alone. Not about whether the entity is new, or dirty, or
    /// turned up in the scan — a dirty entity is held in memory so its edit survives, which is no
    /// reason to keep drawing it over ground the editor has stopped loading. That also makes a created
    /// entity and a modified one the same case, which they were not while "is it loaded" was answered
    /// from a row id an unsaved entity does not have.
    ///
    /// Pure and static so the rules can be tested without an editor around them.
    /// </summary>
    /// <param name="scanned">
    /// Whether the last scan returned this entity. This is what covers the load margin — stored
    /// entities are read over the wider region, so an input just past the view is in the scan.
    /// Deliberately not a second
    /// geometric test against the load region: derived content is produced for the view only, so a
    /// chunk the loader has stopped producing has to go rather than sit in the margin unrefreshed.
    /// </param>
    /// <param name="pinned">Whether the edit session is holding the entity for an uncommitted edit.</param>
    public static EntityFate FateOf(MapId map, Aabb bounds, MapId scanMap, Aabb view, bool scanned, bool pinned)
    {
        if (map == scanMap && bounds.Intersects(view))
        {
            return EntityFate.Visible;
        }

        return scanned || pinned ? EntityFate.Hidden : EntityFate.Unload;
    }

    /// <summary>
    /// Re-judges every entity streaming owns against the last scan. Resident entities (prefab
    /// templates) are skipped: they are not streamed content and there is no region they belong to.
    /// </summary>
    private void ApplyFates()
    {
        var unload = new List<SceneEntity>();
        foreach (SceneEntity entity in _context.Scene.Entities)
        {
            if (_context.Scene.IsResident(entity))
            {
                continue;
            }

            switch (FateOf(entity.Map, entity.WorldBounds, _scanMap, _scanView,
                           _present.Contains(entity), IsPinned(entity)))
            {
                case EntityFate.Visible:
                    _context.Scene.SetPeripheral(entity, false);
                    break;

                case EntityFate.Hidden:
                    Hide(entity);
                    break;

                default:
                    unload.Add(entity);
                    break;
            }
        }

        foreach (SceneEntity entity in unload)
        {
            _context.Selection.Remove(entity);
            _context.Scene.Remove(entity);
            _present.Remove(entity);
        }
    }

    // Out of reach is out of the selection too, whether the entity left the scene or is only being
    // held in memory: otherwise the inspector and the gizmo go on editing something the viewport no
    // longer shows.
    private void Hide(SceneEntity entity)
    {
        _context.Scene.SetPeripheral(entity, true);
        _context.Selection.Remove(entity);
    }

    private bool IsPinned(SceneEntity entity)
    {
        foreach (IEntity pinned in _context.EditSessions.Active.Pinned)
        {
            if (ReferenceEquals(pinned, entity))
            {
                return true;
            }
        }

        return false;
    }

    private void Refresh(SceneEntity loaded, SceneEntity rescanned)
    {
        foreach (ISceneEntityLoader loader in _loaders)
        {
            if (loader.Handles(loaded) && loader.TryRefresh(loaded, rescanned))
            {
                return;
            }
        }
    }

    // The stable identity a re-scan deduplicates on, from whichever source owns the entity.
    private long? KeyOf(SceneEntity entity)
    {
        foreach (ISceneEntityLoader loader in _loaders)
        {
            if (loader.Handles(entity))
            {
                return loader.KeyOf(entity);
            }
        }

        foreach (Storage storage in _context.Database.Storages)
        {
            foreach (ISceneEntityFactory factory in storage.SceneFactories)
            {
                if (factory.Handles(entity))
                {
                    return factory.PersistentKey(entity);
                }
            }
        }

        return null;
    }
}
