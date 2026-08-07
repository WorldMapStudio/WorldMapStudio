using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// The built-in default storage that manages the editor's own entities. Users extend it with new
/// tables and entities (by registering factories into it) or define their own separate storages.
/// Hosts its entity factories, which self-register with [Subsystem(nameof(EditorStorage))].
/// </summary>
[Subsystem(nameof(DatabaseSystem))]
public sealed partial class EditorStorage : Storage, ISubsystemHost
{
    public const string StorageName = "Editor";

    public override string Name => StorageName;

    public EditorStorage(DatabaseSystem database)
    {
        InitializeSubsystems();
    }

    // Default to an editor-managed dolt instance so a new project works out of the box. Exposed
    // statically so project settings (created before any Storage instance exists) can seed the same
    // defaults.
    public static StorageConnection DefaultConnection() => new()
    {
        LaunchServer = true,
        Port = 3312,
        Database = "editor",
    };

    public override StorageConnection CreateDefaultConnection() => DefaultConnection();

    public override IEnumerable<ISceneEntityFactory> SceneFactories => Subsystems.OfType<ISceneEntityFactory>();

    public override IEnumerable<IMapSource> MapSources => Subsystems.OfType<IMapSource>();

    /// <summary>Opens a short-lived context for one unit of work against this storage.</summary>
    public EditorDbContext CreateContext() => new(BuildOptions<EditorDbContext>());

    public override void EnsureSchema()
    {
        using EditorDbContext context = CreateContext();
        context.Database.EnsureCreated();
    }

    public override Schema? ExpectedSchema()
    {
        using EditorDbContext context = CreateContext();
        return ModelSchema.Extract(context);
    }

    public override async Task CommitAsync(IReadOnlyList<SceneEntity> saves, IReadOnlyList<SceneEntity> deletes)
    {
        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        var writeBacks = new List<Action>();
        foreach (SceneEntity entity in saves)
        {
            if (FactoryFor(entity) is { } factory)
            {
                writeBacks.Add(factory.Stage(context, entity));
            }
        }

        foreach (SceneEntity entity in deletes)
        {
            FactoryFor(entity)?.StageDelete(context, entity);
        }

        // A single SaveChanges wraps all staged inserts/updates/deletes in one transaction.
        await context.SaveChangesAsync().ConfigureAwait(false);

        foreach (Action writeBack in writeBacks)
        {
            writeBack();
        }
    }
}
