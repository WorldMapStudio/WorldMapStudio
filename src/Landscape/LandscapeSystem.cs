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

    /// <summary>Streams this map's chunks. Registered into <see cref="StreamingSystem"/> by the context.</summary>
    public LandscapeChunkLoader ChunkLoader => _chunkLoader ??= new LandscapeChunkLoader(this);

    /// <summary>Rebuilds loaded chunks when the entities or catalog that shape them change.</summary>
    public LandscapeRebuilder Rebuilder => _rebuilder ??= new LandscapeRebuilder(_context);

    private LandscapeChunkLoader? _chunkLoader;
    private LandscapeRebuilder? _rebuilder;

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

    /// <summary>
    /// Builds chunks from stored data: deformers come from scanning storage, so this reaches maps that
    /// are not open and chunks that never streamed in. The offline counterpart of
    /// <see cref="TakeSnapshot"/>, which captures the *live* build inputs from the loaded scene and
    /// must be called on the main thread.
    ///
    /// One scene scan and one <see cref="LandscapeBuilder.Build"/> call cover every requested chunk —
    /// a caller writing a whole tile needs its chunks built as a block, not scanned once each. Returns
    /// every requested chunk's problems alongside its output, so no second pass is needed to surface
    /// them.
    /// </summary>
    public async Task<LandscapeBuildResult?> BuildFromStorageAsync(MapId map, IReadOnlyList<ChunkCoord> coords)
    {
        if (coords.Count == 0)
        {
            return null;
        }

        LandscapeSettings? settings = LoadSettingsFor(map);
        if (settings == null)
        {
            return null;
        }

        var builder = new LandscapeBuilder(settings, Catalog, Functions);
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
        return builder.Build(coords, deformers);
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

    private LandscapeCatalog? _catalog;
    private int _catalogVersion = -1;
    private int _functionVersion = -1;
    private MapId _catalogMap = new(-1);

    /// <summary>
    /// The loaded catalog, as the resolver sees it. Channels, layers and materials are all scoped to
    /// the open map. Rebuilt only when entities are added or removed, functions change, or the open
    /// map changes — edits to an entity's fields show through the references, and the window reads
    /// this several times a frame.
    /// </summary>
    public LandscapeCatalog Catalog
    {
        get
        {
            MapId map = _context.Maps.CurrentMap;
            if (_catalog != null && _catalogVersion == _context.Catalog.Version
                && _functionVersion == Functions.Version && _catalogMap.Equals(map))
            {
                return _catalog;
            }

            _catalogVersion = _context.Catalog.Version;
            _functionVersion = Functions.Version;
            _catalogMap = map;
            _catalog = new LandscapeCatalog(
                _context.Catalog.OfType<LandscapeChannel>().Where(channel => channel.Map.Equals(map)).ToList(),
                _context.Catalog.OfType<LandscapeLayer>().Where(layer => layer.Map.Equals(map)).ToList(),
                _context.Catalog.OfType<LandscapeMaterial>().Where(material => material.Map.Equals(map)).ToList(),
                Functions);
            return _catalog;
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
        _catalog = null;
        _catalogVersion = -1;
        _functionVersion = -1;
        _catalogMap = new MapId(-1);
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
            Reporter.KeepOnly(_context.Scene.Entities.OfType<LandscapeChunk>().Select(chunk => chunk.Coord).ToList());
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

        return Save(profile.CreateSettings());
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
