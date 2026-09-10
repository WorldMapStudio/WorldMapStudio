using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Owns the landscape of the open map: its settings, and the catalog (channels, layers, materials)
/// that chunks are resolved against. A plain member of <see cref="EditorContext"/> — the core spine,
/// not an extension point — but itself a host, so plugins register their
/// <see cref="ILandscapeProfile"/>s into it.
///
/// A map without landscape settings simply has no terrain; <see cref="IsEnabled"/> is false and
/// nothing builds. Enabling one is a deliberate act (pick a profile), because the settings decide how
/// terrain is represented for the life of the map.
/// </summary>
public sealed partial class LandscapeSystem : ISubsystemHost, IWorldParticipant
{
    private readonly EditorContext _context;

    public LandscapeSystem(EditorContext context)
    {
        _context = context;
        InitializeSubsystems();

        // Discovered here rather than in Load(): the registry is code, not project data, so it does
        // not wait on the database or the migration gate.
        Functions.Discover();
    }

    public EditorContext Context => _context;

    /// <summary>The export-target profiles available when setting up a map's landscape.</summary>
    public IEnumerable<ILandscapeProfile> Profiles => Subsystems.OfType<ILandscapeProfile>();

    /// <summary>The alpha and height functions materials can bind, discovered by reflection.</summary>
    public LandscapeFunctions Functions { get; } = new();

    /// <summary>Streams this map's terrain batches. Registered into <see cref="StreamingSystem"/> by the context.</summary>
    public LandscapeBatchLoader BatchLoader => _batchLoader ??= new LandscapeBatchLoader(this);

    /// <summary>Rebuilds loaded chunks when the entities or catalog that shape them change.</summary>
    public LandscapeRebuilder Rebuilder => _rebuilder ??= new LandscapeRebuilder(_context);

    /// <summary>Coord→chunk lookup over the loaded chunks, for callers that would otherwise scan them all.</summary>
    public LandscapeChunkIndex ChunkIndex => _chunkIndex ??= new LandscapeChunkIndex(_context.Scene);

    private LandscapeBatchLoader? _batchLoader;
    private LandscapeRebuilder? _rebuilder;
    private LandscapeChunkIndex? _chunkIndex;

    // Compared as a pair, never hashed into one int. Two counters fit in a tuple exactly, so folding
    // them into a hash buys nothing and costs a class of bug that cannot be debugged: a collision, or
    // a real hash landing on the -1 "nothing yet" sentinel, is a silently skipped report or rebuild.
    private (int Registry, int Content) _reportedCatalog = (-1, -1);
    private int _reportedScene = -1;
    private (int Landscape, int Catalog) _fallbackKey = (-1, -1);

    /// <summary>The grid of the open map, or null when it has no landscape.</summary>
    public LandscapeGrid? Grid => Settings == null ? null : new LandscapeGrid(Settings);

    /// <summary>
    /// The material a chunk falls back to when nothing claims a base, resolved on the main thread so
    /// background chunk building reads a plain reference instead of walking the catalog.
    /// </summary>
    public LandscapeMaterial? FallbackMaterial { get; private set; }

    private void RefreshFallback() =>
        FallbackMaterial = Settings?.FallbackMaterialId is { } id
            ? Catalog.Materials.FirstOrDefault(material => material.RecordId == id)
            : null;

    /// <summary>Everything a background chunk build needs, captured at one instant.</summary>
    public sealed record BuildSnapshot(
        LandscapeSettings Settings,
        LandscapeCatalog Catalog,
        LandscapeFunctions Functions,
        IReadOnlyList<ILandscapeDeformer> Deformers);

    /// <summary>Everything an offline build reads off live editor state, captured on the main thread.
    /// Reused for as long as neither the settings nor the catalog have moved, which for the length of
    /// a batch is always — the batch holds the gate. <see cref="Settings"/> is null for a map with no
    /// landscape, still cached so repeated tiles for it do not each pay a thread hop.</summary>
    private sealed record OfflineSnapshot(
        int LandscapeVersion,
        int CatalogVersion,
        int FunctionVersion,
        LandscapeSettings? Settings,
        LandscapeCatalog Catalog);

    private readonly object _offlineLock = new();
    private readonly Dictionary<MapId, OfflineSnapshot> _offlineSnapshots = [];

    /// <summary>
    /// Captures the inputs of a build. Called on the main thread before the work leaves it: the
    /// catalog memoizes into fields and the scene registry is mutated by streaming, so a builder must
    /// not read either while the user is editing.
    /// </summary>
    public BuildSnapshot? TakeSnapshot()
    {
        if (Settings is not { } settings)
        {
            return null;
        }

        List<ILandscapeDeformer> deformers = _context.Scene.Entities
            .SelectMany(entity => entity.Components)
            .OfType<ILandscapeDeformer>()
            .ToList();

        return new BuildSnapshot(settings.Clone(), Catalog, Functions, deformers);
    }

    /// <summary>
    /// Any map's settings, not only the open one — offline work (see <see cref="BuildFromStorageAsync"/>)
    /// reaches maps nothing has loaded. The open map answers from <see cref="Settings"/> without a
    /// query, which is what keeps a build loop off the database. Always a clone, since callers mutate
    /// what they get.
    /// </summary>
    public LandscapeSettings? LoadSettingsFor(MapId map)
    {
        if (_context.Maps.CurrentMap == map && Settings is { } loaded)
        {
            return loaded.Clone();
        }

        foreach (ILandscapeSettingsSource source in Sources)
        {
            try
            {
                if (BlockingWork.Run(() => source.LoadAsync(map)) is { } settings)
                {
                    return settings.Clone();
                }
            }
            catch (Exception e)
            {
                GD.PushError($"[Landscape] Loading settings for map {map.Value} failed: {e.Message}");
            }
        }

        return null;
    }

    /// <summary>A storage build's result plus the scene entities it scanned — the built region grown
    /// by the sample halo — handed back so a caller that also needs those entities (a tile's
    /// placements, say) does not query the scene a second time.</summary>
    public sealed record LandscapeStorageBuild(LandscapeBuildResult Result, IReadOnlyList<SceneEntity> SceneEntities);

    /// <summary>
    /// Builds chunks from stored data: deformers come from scanning storage, so this reaches maps that
    /// are not open and chunks that never streamed in. The offline counterpart of
    /// <see cref="TakeSnapshot"/>, which captures the *live* build inputs from the loaded scene and
    /// must be called on the main thread.
    ///
    /// One scene scan and one <see cref="LandscapeBuilder.Build"/> call cover every requested chunk —
    /// a caller writing a whole tile needs its chunks built as a block, not scanned once each. Returns
    /// every requested chunk's problems alongside its output, so no second pass is needed to surface
    /// them, and the scanned entities so no second scan is either.
    /// </summary>
    public async Task<LandscapeStorageBuild?> BuildFromStorageAsync(
        MapId map, IReadOnlyList<ChunkCoord> coords, WorkContext work)
    {
        if (coords.Count == 0)
        {
            return null;
        }

        // Settings and catalog are read off live editor state, so take them on the main thread before
        // the build leaves it — the guarantee TakeSnapshot gives the live path. Cached per map for the
        // length of a batch, so only the first tile of each map pays the hop.
        OfflineSnapshot snapshot = await TakeOfflineSnapshotAsync(map, work).ConfigureAwait(false);
        if (snapshot.Settings is not { } settings)
        {
            return null;
        }

        var builder = new LandscapeBuilder(settings, snapshot.Catalog, Functions);
        Aabb scan = ScanBounds(builder, coords[0]);
        for (int i = 1; i < coords.Count; i++)
        {
            scan = scan.Merge(ScanBounds(builder, coords[i]));
        }

        IReadOnlyList<SceneEntity> entities = await _context.Database.ScanSceneAsync(map, scan).ConfigureAwait(false);
        List<ILandscapeDeformer> deformers = entities
            .SelectMany(entity => entity.Components)
            .OfType<ILandscapeDeformer>()
            .ToList();

        // Storage-scanned deformers are inert until hydrated with the state streaming would have put
        // on them live — resident image pixels, a published procedural build. No-op when nothing
        // scanned needs it.
        await LandscapeBuildPreparation.RunAsync(_context, scan, deformers, work);

        return new LandscapeStorageBuild(builder.Build(coords, deformers), entities);
    }

    /// <summary>
    /// The settings and catalog for an offline build of <paramref name="map"/>, hopping to the main
    /// thread to read live editor state only on a cache miss. During a batch nothing edits either, so
    /// after the first tile of a map this returns without a hop and retires the per-tile
    /// <see cref="LoadSettingsFor"/> database round trip with it.
    /// </summary>
    private async Task<OfflineSnapshot> TakeOfflineSnapshotAsync(MapId map, WorkContext work)
    {
        (int Landscape, int Catalog, int Function) key = (Version, _context.Catalog.Version, Functions.Version);
        lock (_offlineLock)
        {
            if (_offlineSnapshots.TryGetValue(map, out OfflineSnapshot? cached)
                && (cached.LandscapeVersion, cached.CatalogVersion, cached.FunctionVersion) == key)
            {
                return cached;
            }
        }

        await work.SwitchToMain();
        var snapshot = new OfflineSnapshot(
            Version, _context.Catalog.Version, Functions.Version, LoadSettingsFor(map), CatalogFor(map));
        lock (_offlineLock)
        {
            _offlineSnapshots[map] = snapshot;
        }

        await work.SwitchToBackground();
        return snapshot;
    }

    private static Aabb ScanBounds(LandscapeBuilder builder, ChunkCoord coord)
    {
        Aabb bounds = builder.Grid.BoundsOf(coord);
        foreach (ChunkCoord neighbour in builder.Grid.OverlappingWithHalo(bounds, builder.SampleRadius))
        {
            bounds = bounds.Merge(builder.Grid.BoundsOf(neighbour));
        }

        return bounds;
    }

    /// <summary>
    /// Where the viewport is looking. Rebuilds are ordered by distance from it, so what the user is
    /// looking at lands first; the debug window uses it to pick the chunk under the camera.
    /// </summary>
    public Vector3 Focus { get; set; }

    /// <summary>Publishes what the builder finds into the editor's problem list.</summary>
    public LandscapeProblemReporter Reporter => _reporter ??= new LandscapeProblemReporter(_context.Problems);

    private LandscapeProblemReporter? _reporter;

    /// <summary>Settings of the open map, or null when it has no landscape.</summary>
    public LandscapeSettings? Settings { get; private set; }

    /// <summary>Whether the open map has a landscape at all.</summary>
    public bool IsEnabled => Settings != null;

    /// <summary>Bumps whenever the settings or the catalog change, so views can tell when to refresh.</summary>
    public int Version { get; private set; }

    /// <summary>The last load or save error, or null when everything is clean.</summary>
    public string? Error { get; private set; }

    /// <summary>The map whose settings are currently loaded, so a map change can be noticed.</summary>
    private MapId _loadedMap = new(-1);

    private readonly object _catalogLock = new();
    private readonly Dictionary<MapId, (int Catalog, int Functions, LandscapeCatalog Value)> _catalogs = [];

    /// <summary>
    /// The loaded catalog, as the resolver sees it for the open map. Channels, layers and materials
    /// are all scoped to it — the window reads this several times a frame.
    /// </summary>
    public LandscapeCatalog Catalog => CatalogFor(_context.Maps.CurrentMap);

    /// <summary>
    /// Any map's catalog, not only the open one. Channels, layers and materials are all per map, so
    /// offline work reaching another map must resolve against that map's own set — the open map's
    /// would silently resolve nothing.
    ///
    /// Main thread only: it walks the live catalog registry, which the editor mutates. Offline callers
    /// take this during their main-thread hop and carry the result into the build. Rebuilt per map
    /// only when entities are added or removed or functions change — edits to an entity's fields show
    /// through the references.
    /// </summary>
    public LandscapeCatalog CatalogFor(MapId map)
    {
        lock (_catalogLock)
        {
            if (_catalogs.TryGetValue(map, out var cached)
                && cached.Catalog == _context.Catalog.Version
                && cached.Functions == Functions.Version)
            {
                return cached.Value;
            }

            var built = new LandscapeCatalog(
                _context.Catalog.OfType<LandscapeChannel>().Where(channel => channel.Map.Equals(map)).ToList(),
                _context.Catalog.OfType<LandscapeLayer>().Where(layer => layer.Map.Equals(map)).ToList(),
                _context.Catalog.OfType<LandscapeMaterial>().Where(material => material.Map.Equals(map)).ToList(),
                Functions,
                _context.Catalog.OfType<TerrainAttribute>().Where(attribute => attribute.Map.Equals(map)).ToList(),
                _context.Catalog.OfType<LandscapeMaterialAttributeWrite>().Where(write => write.Map.Equals(map)).ToList());

            _catalogs[map] = (_context.Catalog.Version, Functions.Version, built);
            return built;
        }
    }

    private IEnumerable<ILandscapeSettingsSource> Sources =>
        _context.Database.Storages.SelectMany(storage => storage.LandscapeSettingsSources);

    /// <summary>
    /// Loads the open map's settings. Called when the editor opens — after the migration gate, like
    /// <see cref="MapSystem.Load"/>, since the tables may not exist until it has run. The landscape
    /// catalog itself is loaded earlier, by <see cref="DatabaseSystem"/>'s own <see cref="IWorldParticipant"/>.
    /// </summary>
    public void Load()
    {
        Error = null;
        LoadSettings(_context.Maps.CurrentMap);
        Version++;
    }

    // Landscape has to load after maps (settings are per current-map) and before everything that
    // resolves against its catalog (mesh materials, procedural models, images all bind channels).
    float IWorldParticipant.LoadPriority => 1f;

    string? IWorldParticipant.LoadStep => "Loading landscape";

    void IWorldParticipant.LoadWorld() => Load();

    // A rebuild in flight is background work applying chunk meshes into the scene registry — exactly
    // what a reload's quiescence wait exists to not race, the same reasoning as StreamingSystem's own
    // pending scan.
    bool IWorldParticipant.IsBusy => Rebuilder.IsBuilding;

    void IWorldParticipant.UnloadWorld()
    {
        _context.Database.UnloadCatalog<LandscapeMaterial>();

        Settings = null;
        FallbackMaterial = null;
        Error = null;
        _loadedMap = new MapId(-1);
        lock (_catalogLock)
        {
            _catalogs.Clear();
        }

        lock (_offlineLock)
        {
            _offlineSnapshots.Clear();
        }

        _reportedCatalog = (-1, -1);
        _reportedScene = -1;
        _fallbackKey = (-1, -1);

        Rebuilder.Reset();
        Reporter.ClearAll();
        Version++;
    }

    /// <summary>Notices a map change and swaps to that map's settings. Cheap to call every frame.</summary>
    public void Update()
    {
        // Resolve the fallback here, on the main thread, whenever the settings or the catalog moved —
        // background chunk building must not walk the catalog while the user is editing it.
        if (_fallbackKey != (Version, _context.Catalog.Version))
        {
            _fallbackKey = (Version, _context.Catalog.Version);
            RefreshFallback();
        }

        MapId map = _context.Maps.CurrentMap;
        if (!map.Equals(_loadedMap))
        {
            LoadSettings(map);

            // The chunks about to stream in belong to another map; nothing loaded is comparable.
            Rebuilder.Reset();
            Reporter.ClearAll();
            _reportedCatalog = (-1, -1);
        }

        if (IsEnabled)
        {
            (int, int) catalog = (_context.Catalog.Version, Catalog.ContentVersion);
            if (_reportedCatalog != catalog)
            {
                _reportedCatalog = catalog;
                Reporter.ReportCatalog(Catalog.Validate());
            }
        }

        // Problems belong to chunks; a chunk that streamed out has nothing left to be wrong with.
        // Only worth checking when chunks actually came or went.
        if (_reportedScene != _context.Scene.Version)
        {
            _reportedScene = _context.Scene.Version;
            Reporter.KeepOnly(_context.Scene.Entities.OfType<LandscapeTerrainBatch>().SelectMany(batch => batch.Chunks.Keys).ToList());
        }

        Rebuilder.Update(Focus);
    }


    /// <summary>
    /// Gives the open map a landscape built from a profile, and saves it. Does nothing if the map
    /// already has one — replacing settings is a separate, warned-about act.
    /// </summary>
    public string? Enable(ILandscapeProfile profile)
    {
        if (IsEnabled)
        {
            return "This map already has a landscape.";
        }

        string? error = Save(profile.CreateSettings());
        if (error == null)
        {
            SeedAttributes(_context.Maps.CurrentMap, profile);
        }

        return error;
    }

    /// <summary>
    /// Creates <paramref name="profile"/>'s declared terrain attributes (and their value names) for
    /// <paramref name="map"/>, skipping any key that already exists so a re-run — or a hand-added
    /// attribute of the same key — is left alone. Committed straight to storage, not through the edit
    /// session, since this is setup rather than an undoable step.
    ///
    /// Called both from <see cref="Enable"/> and from an importer that writes settings directly (so a
    /// map set up by an import still gets the profile's attributes). Safe to call for any map, not
    /// just the open one.
    /// </summary>
    public void SeedAttributes(MapId map, ILandscapeProfile profile)
    {
        IReadOnlyList<TerrainAttributeSeed> seeds = profile.SeedAttributes();
        if (seeds.Count == 0)
        {
            return;
        }

        var created = new List<IEntity>();

        foreach (TerrainAttributeSeed seed in seeds)
        {
            if (_context.Catalog.OfType<TerrainAttribute>().Any(a => a.Map.Equals(map) && a.Key == seed.Attribute.Key))
            {
                continue;
            }

            TerrainAttribute attribute = seed.Attribute;
            attribute.Map = map;
            attribute.Seeded = true;
            _context.Catalog.AssignId(attribute);
            _context.Catalog.Add(attribute);
            created.Add(attribute);

            foreach (TerrainAttributeValueSeed value in seed.Values)
            {
                var row = new TerrainAttributeValue
                {
                    Map = map,
                    AttributeId = attribute.RecordId ?? 0,
                    Value = value.Value,
                    Name = value.Name,
                };
                _context.Catalog.AssignId(row);
                _context.Catalog.Add(row);
                created.Add(row);
            }
        }

        if (created.Count == 0)
        {
            return;
        }

        try
        {
            BlockingWork.Run(() =>
                _context.Database.Storages.OfType<EditorStorage>().First().CommitAsync(created, []));
        }
        catch (Exception e)
        {
            GD.PushError($"[Landscape] Seeding attributes for map {map.Value} failed: {e.Message}");
        }
    }

    /// <summary>Writes settings for the open map, returning an error or null.</summary>
    public string? Save(LandscapeSettings settings)
    {
        if (settings.Validate() is { Count: > 0 } problems)
        {
            return problems[0];
        }

        if (Sources.FirstOrDefault(source => source.CanEdit) is not { } source)
        {
            return "No storage accepts landscape settings.";
        }

        MapId map = _context.Maps.CurrentMap;

        try
        {
            BlockingWork.Run(() => source.SaveAsync(map, settings));
        }
        catch (Exception e)
        {
            GD.PushError($"[Landscape] Saving settings for map {map.Value} failed: {e.Message}");
            return e.Message;
        }

        Settings = settings;
        _loadedMap = map;
        Version++;

        // Settings save straight to storage, outside the edit session, so no command carries this to
        // the chunk change log — restamp the map here or an export never learns the terrain moved.
        _context.ChunkChanges.MarkMapChanged(map);
        return null;
    }

    /// <summary>What a pending settings edit would cost, against what is currently saved.</summary>
    public LandscapeChangeCost CostOf(LandscapeSettings pending) =>
        Settings == null ? LandscapeChangeCost.Free : LandscapeSettings.ChangeCost(Settings, pending);

    private void LoadSettings(MapId map)
    {
        _loadedMap = map;
        Settings = null;

        foreach (ILandscapeSettingsSource source in Sources)
        {
            try
            {
                // First source with settings for the map wins, matching how MapSystem resolves maps.
                if (BlockingWork.Run(() => source.LoadAsync(map)) is { } settings)
                {
                    Settings = settings;
                    break;
                }
            }
            catch (Exception e)
            {
                Error = e.Message;
                GD.PushError($"[Landscape] Loading settings for map {map.Value} failed: {e.Message}");
            }
        }

        Version++;
    }
}
