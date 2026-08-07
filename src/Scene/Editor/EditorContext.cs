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

    /// <summary>Owns the active edit session and its undo history.</summary>
    public EditSessionManager EditSessions { get; }

    /// <summary>The scene entities currently loaded into the editor.</summary>
    public SceneEntityRegistry Scene { get; }

    /// <summary>Owns the active editor tool.</summary>
    public ToolSystem Tools { get; }

    /// <summary>Hosts the data backends (storages) and their entity factories.</summary>
    public DatabaseSystem Database { get; }

    /// <summary>The map currently being viewed and edited.</summary>
    public MapSystem Maps { get; }

    /// <summary>Streams scene entities in and out of the registry as the viewport focus moves.</summary>
    public StreamingSystem Streaming { get; }

    public EditorContext(Node3D root, Project project)
    {
        Root = root;
        Project = project;
        Selection = new SelectionSystem();
        EditSessions = new EditSessionManager();
        Scene = new SceneEntityRegistry();
        Tools = new ToolSystem(this);
        Maps = new MapSystem();
        Database = new DatabaseSystem(this);
        Streaming = new StreamingSystem(this);
        InitializeSubsystems();
        Database.Startup();

        // Persist the project now that storages have seeded their default connections into it.
        ProjectStore.Save(Project);
    }
}
