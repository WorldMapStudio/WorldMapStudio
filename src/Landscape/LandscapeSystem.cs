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
public sealed partial class LandscapeSystem : ISubsystemHost
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

    /// <summary>The export-target profiles available when setting up a map's landscape.</summary>
    public IEnumerable<ILandscapeProfile> Profiles => Subsystems.OfType<ILandscapeProfile>();

    /// <summary>The alpha and height functions materials can bind, discovered by reflection.</summary>
    public LandscapeFunctions Functions { get; } = new();

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

    /// <summary>
    /// The loaded catalog, as the resolver sees it. Rebuilt only when entities are added or removed —
    /// edits to an entity's fields show through the references, and the window reads this several
    /// times a frame.
    /// </summary>
    public LandscapeCatalog Catalog
    {
        get
        {
            if (_catalog != null && _catalogVersion == _context.Catalog.Version && _functionVersion == Functions.Version)
            {
                return _catalog;
            }

            _catalogVersion = _context.Catalog.Version;
            _functionVersion = Functions.Version;
            _catalog = new LandscapeCatalog(
                _context.Catalog.OfType<LandscapeChannel>().ToList(),
                _context.Catalog.OfType<LandscapeLayer>().ToList(),
                _context.Catalog.OfType<LandscapeTextureMaterial>().ToList(),
                Functions);
            return _catalog;
        }
    }

    private IEnumerable<ILandscapeSettingsSource> Sources =>
        _context.Database.Storages.SelectMany(storage => storage.LandscapeSettingsSources);

    /// <summary>
    /// Loads the landscape catalog and the open map's settings. Called when the editor opens — after
    /// the migration gate, like <see cref="MapSystem.Load"/>, since the tables may not exist until it
    /// has run.
    /// </summary>
    public void Load()
    {
        Error = null;
        LoadCatalog();
        LoadSettings(_context.Maps.CurrentMap);
        Version++;
    }

    /// <summary>Re-reads the catalog from every storage, replacing what is loaded.</summary>
    public void LoadCatalog()
    {
        _context.Database.LoadCatalog<LandscapeChannel>();
        _context.Database.LoadCatalog<LandscapeLayer>();
        _context.Database.LoadCatalog<LandscapeTextureMaterial>();
        Version++;
    }

    /// <summary>Notices a map change and swaps to that map's settings. Cheap to call every frame.</summary>
    public void Update()
    {
        MapId map = _context.Maps.CurrentMap;
        if (!map.Equals(_loadedMap))
        {
            LoadSettings(map);
        }
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
            Run(() => source.SaveAsync(map, settings));
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
                if (Run(() => source.LoadAsync(map)) is { } settings)
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

    // Blocking DB work must run off the Godot main thread's synchronization context, or a
    // continuation deadlocks trying to resume on the thread we are blocking (see MigrationSystem).
    private static T Run<T>(Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();

    private static void Run(Func<Task> work) => Task.Run(work).GetAwaiter().GetResult();
}
