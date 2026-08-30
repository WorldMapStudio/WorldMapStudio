using System;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Root of the editor's subsystem tree. Owns the project and the shared systems (selection, and
/// later the database and edit sessions), and hosts the menu bar. Constructed once by <see cref="Editor"/>.
/// </summary>
public sealed partial class EditorContext : ISubsystemHost
{
    public Node3D Root { get; }

    public Project Project { get; }

    public AxisConvention Axes => Project.AxisConvention;

    /// <summary>Shared selection, constructed before the subsystem tree so windows can capture it.</summary>
    public SelectionSystem Selection { get; }

    /// <summary>Last copied scene entities, ready to paste. Shared by every tool, since selection is too.</summary>
    public SceneClipboard Clipboard { get; }

    /// <summary>Where the mouse currently projects into the world within the viewport, updated by the
    /// viewport every frame it draws. Paste reads this to place entities under the cursor.</summary>
    public ViewportPointer Pointer { get; }

    /// <summary>Editor-wide keyboard shortcuts, shared by menus, windows, and tools.</summary>
    public ShortcutSystem Shortcuts { get; }

    /// <summary>Editor-wide viewport display toggles (e.g. grid visibility), shared by the View menu.</summary>
    public ViewSettings View { get; }

    /// <summary>The editor's day clock, advanced once per frame. See <see cref="WorldClock"/>.</summary>
    public WorldClock Clock { get; } = new();

    /// <summary>Owns the active edit session and its undo history.</summary>
    public EditSessionManager EditSessions { get; }

    /// <summary>The scene entities currently loaded into the editor.</summary>
    public SceneEntityRegistry Scene { get; }

    /// <summary>Registered scene component kinds, driving the inspector's add-menu and per-component
    /// drawing. Plugins add their own kinds here instead of the inspector naming them.</summary>
    public SceneComponentRegistry ComponentTypes { get; private set; } = null!;

    /// <summary>The catalog entities currently loaded into the editor.</summary>
    public CatalogEntityRegistry Catalog { get; }

    /// <summary>Everything the editor wants to tell the user about, from any system.</summary>
    public ProblemSystem Problems { get; }

    /// <summary>Lets any window ask the viewport to look somewhere.</summary>
    public ViewportFocus Focus { get; }

    /// <summary>Owns the active editor tool.</summary>
    public ToolSystem Tools { get; }

    /// <summary>Hosts the data backends (storages) and their entity factories.</summary>
    public DatabaseSystem Database { get; }

    /// <summary>The known maps and which one is currently open.</summary>
    public MapSystem Maps { get; }

    /// <summary>Registered model formats (what kind of model a path or a procedural mesh is).</summary>
    public ModelFormatSystem ModelFormats { get; }

    /// <summary>Lists and loads project-configured assets.</summary>
    public AssetSystem Assets { get; }

    /// <summary>Registered mesh material types, the built-material cache, and the saved material preset catalog.</summary>
    public MeshMaterialSystem MeshMaterials { get; }

    /// <summary>The open map's landscape settings and the catalog chunks resolve against.</summary>
    public LandscapeSystem Landscape { get; }

    /// <summary>Builds scene procedural meshes from authored graph components.</summary>
    public ProceduralSystem Procedural { get; }

    /// <summary>Owns the saved prefab library: catalog rows plus their scene-entity templates.</summary>
    public PrefabSystem Prefabs { get; }

    /// <summary>Streams scene entities in and out of the registry as the viewport focus moves.</summary>
    public StreamingSystem Streaming { get; }

    /// <summary>Blends loaded <see cref="IEnvironmentSource"/> components (lights, sky) against the
    /// viewport focus and <see cref="Clock"/> into the active <see cref="EnvironmentValues"/>.</summary>
    public EnvironmentSystem Environments { get; private set; } = null!;

    /// <summary>Compares each storage's expected schema to the live database and drives migrations.</summary>
    public MigrationSystem Migrations { get; }

    /// <summary>Hosts the JS-scriptable surface (console, and later the HTTP/MCP endpoint).</summary>
    public ScriptingSystem Scripting { get; }

    /// <summary>Hosts chunk-oriented export scripts and the committed chunk change registry.</summary>
    public ExportSystem Exports { get; }

    public EditorContext(Node3D root, Project project)
    {
        Root = root;
        Project = project;
        Shortcuts = new ShortcutSystem();
        Selection = new SelectionSystem();
        Clipboard = new SceneClipboard();
        Pointer = new ViewportPointer();
        View = new ViewSettings();
        EditSessions = new EditSessionManager();
        Scene = new SceneEntityRegistry();
        Catalog = new CatalogEntityRegistry();
        Problems = new ProblemSystem();
        Focus = new ViewportFocus();
        Tools = new ToolSystem(this);
        Maps = new MapSystem(this);
        ModelFormats = new ModelFormatSystem(this);
        Assets = new AssetSystem(this);
        MeshMaterials = new MeshMaterialSystem(this);
        Database = new DatabaseSystem(this);
        Landscape = new LandscapeSystem(this);
        Procedural = new ProceduralSystem(this);
        Prefabs = new PrefabSystem(this);

        // Built after Assets/Landscape/Procedural: the built-in component types capture them.
        ComponentTypes = new SceneComponentRegistry(this);

        Streaming = new StreamingSystem(this);
        Environments = new EnvironmentSystem(this);
        Migrations = new MigrationSystem(this);
        Scripting = new ScriptingSystem(this);
        Exports = new ExportSystem(this);

        // Chunks stream like any other scene entity, but they are generated rather than stored, so
        // the landscape hands streaming a loader instead of a storage factory.
        Streaming.AddLoader(Landscape.ChunkLoader);

        // Bound here rather than injected, because the session manager is constructed before the
        // database and streaming system it depends on. From now on committing a session persists it,
        // whichever caller (menu, script, HTTP) asked, and releasing its pins forces a rescan so
        // entities that only stayed loaded for the edit can unload.
        EditSessions.BindStore(Database);
        EditSessions.BindStreaming(Streaming);

        InitializeSubsystems();
    }

    /// <summary>
    /// Runs the blocking startup work: launching the dolt servers, ensuring databases and schemas,
    /// and checking for schema drift. Kept out of the constructor so it can run off the main thread
    /// (see <see cref="LoadingScreen"/>) rather than freezing the UI while the editor opens.
    /// <paramref name="confirmKillStray"/> is forwarded to <see cref="DatabaseSystem.Startup"/> — see
    /// there for why a dolt launch can need it.
    /// </summary>
    public void Startup(Action<string>? onStep = null, Func<string, bool>? confirmKillStray = null)
    {
        onStep?.Invoke("Starting database");
        Database.Startup(confirmKillStray);

        // Persist the project now that storages have seeded their default connections into it.
        ProjectStore.Save(Project);

        // Surface any schema drift between the code and the live database.
        onStep?.Invoke("Checking schema");
        Migrations.Check();
    }

    /// <summary>
    /// Reads the project's content: the maps, then the open map's landscape settings and catalog.
    /// Kept out of <see cref="Startup"/> because the migration gate runs between the two, and these
    /// tables may not exist until it has.
    ///
    /// Off the main thread, like <see cref="Startup"/> and for the same reason — these are the two
    /// largest reads in the open sequence, and running them from <c>Editor.Start()</c> froze the
    /// window for as long as they took. Nothing here touches a Godot node, and the editor scene is not
    /// running yet, so nothing else is reading what this fills in.
    /// </summary>
    public void LoadContent(Action<string>? onStep = null)
    {
        onStep?.Invoke("Loading maps");
        Maps.Load();

        // Needs the current map, so it follows the maps.
        onStep?.Invoke("Loading landscape");
        Landscape.Load();

        onStep?.Invoke("Loading mesh materials");
        MeshMaterials.LoadCatalog();

        // Models bind material presets, so presets load first.
        onStep?.Invoke("Loading procedural models");
        Procedural.LoadCatalog();

        onStep?.Invoke("Loading prefabs");
        Prefabs.LoadCatalog();
        Prefabs.LoadLibrary();

        // Plugin-owned catalogs (e.g. the WoW plugin's light param sets) the core has no field for.
        foreach (ICatalogAutoLoader loader in Subsystems.OfType<ICatalogAutoLoader>())
        {
            loader.Load();
        }
    }
}
