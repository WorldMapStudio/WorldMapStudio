using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;

namespace WorldMapStudio;

/// <summary>
/// Round trips through the editor's entity tables: native entities with tags and components, the
/// attachment rows of an entity stored elsewhere, and what a map delete takes with it. Each test runs
/// against a scratch database on the server the editor's storage connects to and drops it afterwards, so
/// no project's data is touched; without a reachable server they are skipped.
/// </summary>
public static class EntityStorageTests
{
    private const string ScratchDatabase = "__wms_entity_storage_test";
    private const string BridgeSource = "test.source";

    private static readonly Aabb Everywhere = new(new Vector3(-1.0e6f, -1.0e6f, -1.0e6f), new Vector3(2.0e6f, 2.0e6f, 2.0e6f));

    // An entity some other table stores: it carries editor data only through the bridge.
    private sealed class ExternalEntity : SceneEntity
    {
    }

    private sealed class Scratch : IDisposable
    {
        private readonly StorageConnection _connection;

        private Scratch(EditorStorage storage, StorageConnection connection)
        {
            Storage = storage;
            _connection = connection;
        }

        public EditorStorage Storage { get; }

        public static Scratch Open()
        {
            var context = new EditorContext(new Node3D(), new Project { Name = "__wms_entity_storage_test__" });
            EditorStorage storage = context.Database.EditorStorage;
            StorageConnection connection = storage.Connection;
            connection.LaunchServer = false;
            connection.Database = ScratchDatabase;

            try
            {
                Run(connection, $"DROP DATABASE IF EXISTS `{ScratchDatabase}`; CREATE DATABASE `{ScratchDatabase}`;");
            }
            catch (Exception e)
            {
                Assert.Skip($"no database server to run against: {e.Message}");
            }

            storage.EnsureSchema();
            return new Scratch(storage, connection);
        }

        public void Dispose() => Run(_connection, $"DROP DATABASE IF EXISTS `{ScratchDatabase}`;");

        private static void Run(StorageConnection connection, string sql)
        {
            using var conn = new MySqlConnection(connection.BuildConnectionString(includeDatabase: false));
            conn.Open();
            foreach (string statement in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                using MySqlCommand command = conn.CreateCommand();
                command.CommandText = statement;
                command.ExecuteNonQuery();
            }
        }
    }

    private static async Task<int> CountAsync<T>(EditorStorage storage) where T : class
    {
        await using EditorDbContext context = storage.CreateContext();
        return await context.Set<T>().CountAsync().ConfigureAwait(false);
    }

    private static void Apply(BridgeCommitResult result)
    {
        foreach (Action writeBack in result.WriteBacks)
        {
            writeBack();
        }
    }

    [EditorTest(Category = "Entity Storage", Thread = TestThread.Background)]
    public static async Task A_native_entity_keeps_its_tags_and_components_through_a_commit_and_a_rescan()
    {
        using Scratch scratch = Scratch.Open();
        EditorStorage storage = scratch.Storage;

        var tag = new EntityTagDefinition { RecordId = 1, Name = "town" };
        var entity = new MapSceneEntity { Name = "Lamp", Map = new MapId(7) };
        entity.AddComponent(new MarkerComponent { Shape = MarkerShape.Sphere });
        entity.Tags = EntityTagSet.From([1]);

        await storage.CommitAsync([tag, entity], []).ConfigureAwait(false);

        Assert.IsNotNull(entity.RecordId, "committing gives the entity its identity row");
        Assert.IsTrue(entity.PersistedTags == entity.Tags, "what was written is what later commits diff against");

        MapSceneEntityFactory factory = storage.SceneFactories.OfType<MapSceneEntityFactory>().Single();
        SceneEntityScan scan = await factory.ScanAsync(new MapId(7), Everywhere, new HashSet<long>(), publishing: false).ConfigureAwait(false);
        SceneEntity loaded = scan.Built.Single();

        Assert.IsTrue(loaded is MapSceneEntity);
        Assert.AreEqual("Lamp", loaded.Name);
        Assert.AreEqual("1", string.Join(",", loaded.Tags));
        Assert.IsTrue(loaded.PersistedTags == loaded.Tags);
        Assert.AreEqual(MarkerShape.Sphere, loaded.Attached<MarkerComponent>()!.Shape);

        await storage.CommitAsync([], [entity]).ConfigureAwait(false);

        Assert.AreEqual(0, await CountAsync<EntityRecord>(storage).ConfigureAwait(false), "one delete removes the identity");
        Assert.AreEqual(0, await CountAsync<MapEntityRecord>(storage).ConfigureAwait(false));
        Assert.AreEqual(0, await CountAsync<SceneMarkerComponentRecord>(storage).ConfigureAwait(false), "components cascade");
        Assert.AreEqual(0, await CountAsync<EntityTagRecord>(storage).ConfigureAwait(false), "tag rows cascade");
        Assert.AreEqual(1, await CountAsync<TagDefinitionRecord>(storage).ConfigureAwait(false), "the tag itself stays");
    }

    [EditorTest(Category = "Entity Storage", Thread = TestThread.Background)]
    public static async Task A_bridged_entity_gets_a_row_keeps_its_data_and_loses_the_row_with_it()
    {
        using Scratch scratch = Scratch.Open();
        EditorStorage storage = scratch.Storage;
        await storage.CommitAsync([new EntityTagDefinition { RecordId = 1, Name = "town" }], []).ConfigureAwait(false);

        var external = new ExternalEntity { Tags = EntityTagSet.From([1]) };
        external.AddComponent(new MarkerComponent());

        BridgeCommitResult saved = await storage.CommitBridgedAsync([new BridgedEntity(external, BridgeSource, 555)], []).ConfigureAwait(false);
        Apply(saved);

        int id = external.RecordId!.Value;
        Assert.AreEqual((BridgeSource, 555L, id), saved.Added.Single());
        await using (EditorDbContext read = storage.CreateContext())
        {
            EntityRecord row = await read.Entities.SingleAsync().ConfigureAwait(false);
            Assert.AreEqual(BridgeSource, row.Source);
            Assert.AreEqual(555L, row.SourceKey);

            var fresh = new ExternalEntity();
            await storage.Attachments.LoadAsync(read, new Dictionary<int, SceneEntity> { [id] = fresh }, [id], new SceneEntityScanCatalog(false)).ConfigureAwait(false);
            Assert.AreEqual("1", string.Join(",", fresh.Tags), "a rescan restores the tag");
            Assert.IsNotNull(fresh.Attached<MarkerComponent>(), "and the attached component");
        }

        BridgeCommitResult removed = await storage.CommitBridgedAsync([], [new BridgedEntity(external, BridgeSource, 555)]).ConfigureAwait(false);
        Apply(removed);

        Assert.AreEqual((BridgeSource, 555L), removed.Removed.Single());
        Assert.IsNull(external.RecordId);
        Assert.AreEqual(0, await CountAsync<EntityRecord>(storage).ConfigureAwait(false));
        Assert.AreEqual(0, await CountAsync<EntityTagRecord>(storage).ConfigureAwait(false));
        Assert.AreEqual(0, await CountAsync<SceneMarkerComponentRecord>(storage).ConfigureAwait(false));
    }

    [EditorTest(Category = "Entity Storage", Thread = TestThread.Background)]
    public static async Task A_bridged_commit_replaces_a_stale_row_left_on_the_same_key()
    {
        using Scratch scratch = Scratch.Open();
        EditorStorage storage = scratch.Storage;
        await using (EditorDbContext seed = storage.CreateContext())
        {
            seed.Entities.Add(new EntityRecord { Id = 50, Source = BridgeSource, SourceKey = 777 });
            await seed.SaveChangesAsync().ConfigureAwait(false);
        }

        var external = new ExternalEntity();
        external.AddComponent(new MarkerComponent());

        Apply(await storage.CommitBridgedAsync([new BridgedEntity(external, BridgeSource, 777)], []).ConfigureAwait(false));

        Assert.AreEqual(1, await CountAsync<EntityRecord>(storage).ConfigureAwait(false), "one row per source key");
        Assert.AreNotEqual(50, external.RecordId!.Value, "the new entity has its own identity");
    }

    [EditorTest(Category = "Entity Storage", Thread = TestThread.Background)]
    public static async Task Deleting_a_tag_does_not_break_committing_the_bridged_entities_that_carried_it()
    {
        using Scratch scratch = Scratch.Open();
        EditorStorage storage = scratch.Storage;
        var tag = new EntityTagDefinition { RecordId = 1, Name = "town" };
        await storage.CommitAsync([tag], []).ConfigureAwait(false);

        var external = new ExternalEntity { Tags = EntityTagSet.From([1]) };
        external.AddComponent(new MarkerComponent());
        Apply(await storage.CommitBridgedAsync([new BridgedEntity(external, BridgeSource, 1)], []).ConfigureAwait(false));

        // The tag goes first, taking its links with it through the cascade; the entity, stripped of the
        // tag in the same edit, is committed afterwards and must not trip over the rows that are gone.
        await storage.CommitAsync([], [tag]).ConfigureAwait(false);
        external.Tags = EntityTagSet.Empty;
        Apply(await storage.CommitBridgedAsync([new BridgedEntity(external, BridgeSource, 1)], []).ConfigureAwait(false));

        Assert.IsTrue(external.PersistedTags.IsEmpty);
        Assert.AreEqual(1, await CountAsync<EntityRecord>(storage).ConfigureAwait(false), "the entity keeps its component, so its row stays");
    }

    [EditorTest(Category = "Entity Storage", Thread = TestThread.Background)]
    public static async Task Deleting_a_map_removes_its_entities_and_their_data_but_not_bridged_rows()
    {
        using Scratch scratch = Scratch.Open();
        EditorStorage storage = scratch.Storage;
        var tag = new EntityTagDefinition { RecordId = 1, Name = "town" };
        var native = new MapSceneEntity { Name = "Lamp", Map = new MapId(9) };
        native.AddComponent(new MarkerComponent());
        native.Tags = EntityTagSet.From([1]);
        await storage.CommitAsync([tag, native], []).ConfigureAwait(false);

        var external = new ExternalEntity { Tags = EntityTagSet.From([1]) };
        external.AddComponent(new MarkerComponent());
        Apply(await storage.CommitBridgedAsync([new BridgedEntity(external, BridgeSource, 42)], []).ConfigureAwait(false));

        await storage.CommitTransactionAsync(async context =>
        {
            System.Data.Common.DbTransaction transaction = context.Database.CurrentTransaction!.GetDbTransaction();
            foreach (IMapScopedData owner in storage.MapScopedData)
            {
                await owner.DeleteAsync(context, transaction, new MapId(9)).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        Assert.AreEqual(0, await CountAsync<MapEntityRecord>(storage).ConfigureAwait(false));
        Assert.AreEqual(1, await CountAsync<EntityRecord>(storage).ConfigureAwait(false), "only the bridged row is left");
        Assert.AreEqual(1, await CountAsync<SceneMarkerComponentRecord>(storage).ConfigureAwait(false), "and its component");
        Assert.AreEqual(1, await CountAsync<EntityTagRecord>(storage).ConfigureAwait(false), "and its tag");
    }
}
