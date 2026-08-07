using System.Collections.Generic;
using System.Linq;

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
        InitializeSubsystems();
    }

    // Default to an editor-managed dolt instance so a new project works out of the box.
    public override StorageConnection CreateDefaultConnection() => new()
    {
        LaunchServer = true,
        Port = 3312,
        Database = "editor",
    };

    public override IEnumerable<ISceneEntityFactory> SceneFactories => Subsystems.OfType<ISceneEntityFactory>();

    /// <summary>Opens a short-lived context for one unit of work against this storage.</summary>
    public EditorDbContext CreateContext() => new(BuildOptions<EditorDbContext>());

    public override void EnsureSchema()
    {
        using EditorDbContext context = CreateContext();
        context.Database.EnsureCreated();
    }
}
