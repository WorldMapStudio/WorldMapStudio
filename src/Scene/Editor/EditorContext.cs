using System;
using System.Collections.Generic;
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
    public WorldClock Clock { get; }

    /// <summary>Owns the active edit session and its undo history.</summary>
    public EditSessionManager EditSessions { get; }

    /// <summary>The scene entities currently loaded into the editor.</summary>
    public SceneEntityRegistry Scene { get; }

    /// <summary>Registered scene component kinds, driving the inspector's add-menu and per-component
    /// drawing. Plugins add their own kinds here instead of the inspector naming them.</summary>
    public SceneComponentRegistry ComponentTypes { get; }

    /// <summary>The catalog entities currently loaded into the editor.</summary>
    public CatalogEntityRegistry Catalog { get; }

    /// <summary>Everything the editor wants to tell the user about, from any system.</summary>
    public ProblemSystem Problems { get; }

    /// <summary>When each chunk was last touched by a committed edit. Part of the spine because the
    /// commit path writes to it whether or not anything ever reads it back.</summary>
    public ChunkChangeLog ChunkChanges { get; }

    /// <summary>Lets any window ask the viewport to look somewhere.</summary>
    public ViewportFocus Focus { get; }

    /// <summary>Owns the active editor tool.</summary>
    public ToolSystem Tools { get; }

    /// <summary>Hosts the data backends (storages) and their entity factories.</summary>
    public DatabaseSystem Database { get; }

    /// <summary>Cached display text for catalog reference fields — see <see cref="CatalogReferenceLabels"/>.</summary>
    public CatalogReferenceLabels ReferenceLabels { get; }

    /// <summary>Which <see cref="ICatalogSearchView"/> is available and preferred per catalog — see
    /// <see cref="CatalogSearchViews"/>.</summary>
    public CatalogSearchViews CatalogSearchViews { get; }

    /// <summary>The known maps and which one is currently open.</summary>
    public MapSystem Maps { get; }

    /// <summary>Registered per-map settings sections, drawn in the map picker's properties area.
    /// Plugins add their own kinds here instead of the picker naming them.</summary>
    public MapPropertiesRegistry MapProperties { get; }

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

    /// <summary>Owns the saved image catalog that <see cref="ImageComponent"/> placements reference.</summary>
    public ImageSystem Images { get; }

    /// <summary>Owns the saved prefab library: catalog rows plus their scene-entity templates.</summary>
    public PrefabSystem Prefabs { get; }

    /// <summary>Registered view categories and which are hidden — the per-type viewport visibility
    /// filter behind the "View" menu.</summary>
    public ViewCategorySystem ViewCategories { get; }

    /// <summary>Streams scene entities in and out of the registry as the viewport focus moves.</summary>
    public StreamingSystem Streaming { get; }

    /// <summary>Blends loaded <see cref="IEnvironmentSource"/> components (lights, sky) against the
    /// viewport focus and <see cref="Clock"/> into the active <see cref="EnvironmentValues"/>.</summary>
    public EnvironmentSystem Environments { get; }

    /// <summary>Compares each storage's expected schema to the live database and drives migrations.</summary>
    public MigrationSystem Migrations { get; }

    /// <summary>Hosts the JS-scriptable surface (console, and later the HTTP/MCP endpoint).</summary>
    public ScriptingSystem Scripting { get; }

    /// <summary>Hosts the batch operations and runs one at a time behind <see cref="Operations"/>.</summary>
    public BatchSystem Batch { get; }

    /// <summary>The in-editor test runner, shared by the Test Runner window and scripts. Discovery is
    /// deferred to first use: many tests build their own context, and none of them should pay for it
    /// (or recurse into it).</summary>
    public TestRunner Tests => _tests.Value;

    private readonly Lazy<TestRunner> _tests;

    /// <summary>Collects every <see cref="IWorldParticipant"/> and drives loading/unloading the
    /// project's data as one ordered operation, in both directions.</summary>
    public WorldLifecycle Lifecycle { get; }

    /// <summary>Runs every <see cref="IFrameParticipant"/> once per frame.</summary>
    public FrameLoop Frame { get; }

    /// <summary>The gate exclusive, world-rewriting operations (a batch import, a migration) run
    /// behind, and ordinary editing is refused while one is active.</summary>
    public WorldOperations Operations { get; }

    /// <summary>A reload requested by something that has already reverted in memory (an aborted edit
    /// session, so far) and now wants the guarantee a database re-read gives on top — see
    /// <see cref="EditSessionManager"/>. Consumed once by <see cref="Editor.Update"/>, which is what
    /// actually owns scene transitions.</summary>
    public string? PendingReloadReason { get; private set; }

    /// <summary>Requests a full world reload. Idempotent within one pending request: the first reason
    /// wins until <see cref="Editor"/> consumes it.</summary>
    public void RequestReload(string reason) => PendingReloadReason ??= reason;

    /// <summary>Called by <see cref="Editor"/> once it starts acting on a pending reload.</summary>
    public void ClearPendingReload() => PendingReloadReason = null;

    /// <summary>Whether something (File → Exit, a script) has asked the editor to close. Polled by
    /// <see cref="Editor.Update"/>.</summary>
    public bool ExitRequested { get; private set; }

    public void RequestExit() => ExitRequested = true;

    /// <summary>Whether a <see cref="WorldReload"/> is currently in progress — true from the moment
    /// it starts acting on <see cref="PendingReloadReason"/> until it hands control back to
    /// <see cref="Editor"/>, which is longer than <see cref="PendingReloadReason"/> stays set. What
    /// <c>wms.editor.reload()</c> polls to know when to resolve.</summary>
    public bool IsReloading { get; private set; }

    public void BeginReload() => IsReloading = true;

    public void EndReload() => IsReloading = false;

    private readonly List<object> _spine = [];

    /// <summary>The core systems this constructor builds directly, in construction order. Everything
    /// that walks the editor's systems (<see cref="SubsystemTree"/>) starts from these, since they are
    /// not <see cref="ISubsystem"/>s and nothing else would find them.</summary>
    public IReadOnlyList<object> Spine => _spine;

    private T Add<T>(T member) where T : notnull
    {
        _spine.Add(member);
        return member;
    }

    public EditorContext(Node3D root, Project project)
    {
        Root = root;
        Project = project;
        _tests = new Lazy<TestRunner>(() => new TestRunner(root));
        Clock = Add(new WorldClock());
        Shortcuts = Add(new ShortcutSystem());
        Selection = Add(new SelectionSystem());
        Clipboard = Add(new SceneClipboard());
        Pointer = Add(new ViewportPointer());
        View = Add(new ViewSettings());
        EditSessions = Add(new EditSessionManager(new EditSessionBindings(
            () => Database,
            () => Streaming,
            () => RequestReload("Edit session aborted"),
            () => Operations.ActiveOperation)));
        Scene = Add(new SceneEntityRegistry());
        Catalog = Add(new CatalogEntityRegistry(type => Database.FindRecordIdSource(type)));
        Problems = Add(new ProblemSystem());
        ChunkChanges = Add(new ChunkChangeLog(this));
        Focus = Add(new ViewportFocus());
        Tools = Add(new ToolSystem(this));
        Maps = Add(new MapSystem(this));
        MapProperties = Add(new MapPropertiesRegistry(this));
        ModelFormats = Add(new ModelFormatSystem(this));
        Assets = Add(new AssetSystem(this));
        MeshMaterials = Add(new MeshMaterialSystem(this));
        Database = Add(new DatabaseSystem(this));
        ReferenceLabels = Add(new CatalogReferenceLabels(this));
        CatalogSearchViews = Add(new CatalogSearchViews(this));

        Landscape = Add(new LandscapeSystem(this));
        Procedural = Add(new ProceduralSystem(this));
        Images = Add(new ImageSystem(this));
        Prefabs = Add(new PrefabSystem(this));

        // After ModelFormats/Assets/Landscape/Procedural: a format-driven category source reads them.
        ViewCategories = Add(new ViewCategorySystem(this));

        // Built after Assets/Landscape/Procedural: the built-in component types capture them.
        ComponentTypes = Add(new SceneComponentRegistry(this));

        Streaming = Add(new StreamingSystem(this));
        Environments = Add(new EnvironmentSystem(this));
        Migrations = Add(new MigrationSystem(this));
        Scripting = Add(new ScriptingSystem(this));
        Batch = Add(new BatchSystem(this));

        // Chunks stream like any other scene entity, but they are generated rather than stored, so
        // the landscape hands streaming a loader instead of a storage factory.
        Streaming.AddLoader(Landscape.BatchLoader);

        Lifecycle = Add(new WorldLifecycle(this));
        Frame = Add(new FrameLoop(this));
        Operations = Add(new WorldOperations(this));

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
    /// Reads the project's content: every <see cref="IWorldParticipant"/>, in <see cref="IWorldParticipant.LoadPriority"/>
    /// order — maps, then the open map's landscape settings and catalog, then everything that resolves
    /// against it. Kept out of <see cref="Startup"/> because the migration gate runs between the two,
    /// and these tables may not exist until it has.
    ///
    /// Off the main thread, like <see cref="Startup"/> and for the same reason — these are the two
    /// largest reads in the open sequence, and running them from <c>Editor.Start()</c> froze the
    /// window for as long as they took. Nothing here touches a Godot node, and the editor scene is not
    /// running yet, so nothing else is reading what this fills in.
    /// </summary>
    public void LoadContent(Action<string>? onStep = null) => Lifecycle.Load(onStep);
}
