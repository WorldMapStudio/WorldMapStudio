using System;
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

    /// <summary>Editor-wide keyboard shortcuts, shared by menus, windows, and tools.</summary>
    public ShortcutSystem Shortcuts { get; }

    /// <summary>Editor-wide viewport display toggles (e.g. grid visibility), shared by the View menu.</summary>
    public ViewSettings View { get; }

    /// <summary>Owns the active edit session and its undo history.</summary>
    public EditSessionManager EditSessions { get; }

    /// <summary>The scene entities currently loaded into the editor.</summary>
    public SceneEntityRegistry Scene { get; }

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

    /// <summary>Lists and loads project-configured assets.</summary>
    public AssetSystem Assets { get; }

    /// <summary>The open map's landscape settings and the catalog chunks resolve against.</summary>
    public LandscapeSystem Landscape { get; }

    /// <summary>Streams scene entities in and out of the registry as the viewport focus moves.</summary>
    public StreamingSystem Streaming { get; }

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
        View = new ViewSettings();
        EditSessions = new EditSessionManager();
        Scene = new SceneEntityRegistry();
        Catalog = new CatalogEntityRegistry();
        Problems = new ProblemSystem();
        Focus = new ViewportFocus();
        Tools = new ToolSystem(this);
        Maps = new MapSystem(this);
        Assets = new AssetSystem(this);
        Database = new DatabaseSystem(this);
        Landscape = new LandscapeSystem(this);
        Streaming = new StreamingSystem(this);
        Migrations = new MigrationSystem(this);
        Scripting = new ScriptingSystem(this);
        Exports = new ExportSystem(this);

        // Chunks stream like any other scene entity, but they are generated rather than stored, so
        // the landscape hands streaming a loader instead of a storage factory.
        Streaming.AddLoader(Landscape.ChunkLoader);

        // Bound here rather than injected, because the session manager is constructed before the
        // database it writes through. From now on committing a session persists it, whichever caller
        // (menu, script, HTTP) asked.
        EditSessions.BindStore(Database);

        InitializeSubsystems();
    }

    /// <summary>
    /// Runs the blocking startup work: launching the dolt servers, ensuring databases and schemas,
    /// and checking for schema drift. Kept out of the constructor so it can run off the main thread
    /// (see <see cref="LoadingScreen"/>) rather than freezing the UI while the editor opens.
    /// </summary>
    public void Startup(Action<string>? onStep = null)
    {
        onStep?.Invoke("Starting database");
        Database.Startup();

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
    }
}
