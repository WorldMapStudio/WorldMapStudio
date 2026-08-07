namespace WorldMapStudio;

/// <summary>
/// The built-in default storage that manages the editor's own entities. Users extend it with new
/// tables and entities (by registering factories into it) or define their own separate storages.
/// Hosts its entity factories, which self-register with [Subsystem(nameof(EditorStorage))].
/// </summary>
[Subsystem(nameof(DatabaseSystem))]
public sealed partial class EditorStorage : Storage, ISubsystemHost
{
    public override string Name => "Editor";

    public EditorStorage(DatabaseSystem database)
    {
        // Default to an editor-managed dolt instance so a new project works out of the box.
        Connection.LaunchServer = true;
        Connection.Port = 3312;
        Connection.Database = "editor";
        InitializeSubsystems();
    }
}
