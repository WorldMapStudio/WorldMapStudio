using System;
using System.IO;
using System.Linq;
using Godot;
using Microsoft.Data.Sqlite;

namespace WorldMapStudio;

/// <summary>A storage creates and migrates only the tables it owns; the rest of a shared database file is only validated.</summary>
public static class TableOwnershipTests
{
    private static SchemaColumn Col(string name) => new(name, "INTEGER", false);

    [EditorTest(Category = "Table Ownership", Thread = TestThread.Background)]
    public static void Ensure_schema_creates_owned_tables_beside_foreign_ones()
    {
        string path = Path.Combine(Path.GetTempPath(), $"__wms_ownership_test_{Guid.NewGuid():N}.doltlite");
        try
        {
            var context = new EditorContext(new Node3D(), new Project { Name = "__wms_ownership_test__" });
            EditorStorage storage = context.Database.EditorStorage;
            storage.Location.DatabasePath = path;

            BlockingWork.Run(() => storage.ApplySqlAsync(
                "CREATE TABLE \"dbc_foreign\" (\"id\" INTEGER PRIMARY KEY, \"name\" TEXT); INSERT INTO \"dbc_foreign\" VALUES (1, 'a'), (2, 'b');"));

            storage.EnsureSchema();

            Schema live = BlockingWork.Run(storage.ReadLiveSchemaAsync);
            Assert.IsTrue(live.Tables.ContainsKey("wms_entities"), "owned tables are created in a non-empty file");
            Assert.IsTrue(live.Tables.ContainsKey("dbc_foreign"));

            var changes = SchemaDiff.Compute(storage.ExpectedSchema()!, live, null, storage.OwnsTable);
            Assert.AreEqual(0, changes.Count, "nothing pending after EnsureSchema: " + string.Join("; ", changes.Select(c => c.Describe())));

            using var connection = new SqliteConnection(storage.Location.BuildConnectionString());
            connection.Open();
            using SqliteCommand count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM \"dbc_foreign\"";
            Assert.AreEqual(2L, (long)count.ExecuteScalar()!);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [EditorTest(Category = "Table Ownership")]
    public static void A_foreign_table_is_never_dropped_or_altered()
    {
        var expected = new Schema([new SchemaTable("wms_a", [Col("id")], ["id"], [])]);
        var live = new Schema(
        [
            new SchemaTable("wms_a", [Col("id")], ["id"], []),
            new SchemaTable("dbc_x", [Col("id"), Col("y")], ["id"], []),
        ]);

        var changes = SchemaDiff.Compute(expected, live, null, name => name.StartsWith("wms_", StringComparison.Ordinal));
        Assert.AreEqual(0, changes.Count);

        live = new Schema([new SchemaTable("wms_a", [Col("id")], ["id"], []), new SchemaTable("wms_old", [Col("id")], ["id"], [])]);
        changes = SchemaDiff.Compute(expected, live, null, name => name.StartsWith("wms_", StringComparison.Ordinal));
        Assert.IsTrue(changes.Any(c => c.Kind == SchemaChangeKind.DropTable && c.Table == "wms_old"), "an unused owned table is still dropped");
    }

    [EditorTest(Category = "Table Ownership")]
    public static void Differences_in_a_non_owned_table_are_mismatches_without_sql()
    {
        var expected = new Schema(
        [
            new SchemaTable("dbc_a", [Col("id"), Col("x")], ["id"], []),
            new SchemaTable("dbc_missing", [Col("id")], ["id"], []),
        ]);
        var live = new Schema([new SchemaTable("dbc_a", [Col("id"), Col("old")], ["id"], [])]);

        var changes = SchemaDiff.Compute(expected, live, null, _ => false);

        Assert.AreEqual(3, changes.Count);
        Assert.IsTrue(changes.All(c => c.IsMismatch));
        Assert.IsTrue(changes.Any(c => c.Table == "dbc_missing"));
        Assert.AreEqual(string.Empty, MigrationSql.Generate(changes).Trim());
    }
}
