using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>Covers schema introspection and diffing used by the migration flow.</summary>
public static class SchemaTests
{
    [EditorTest(Category = "Schema")]
    public static void Model_schema_matches_the_editor_context()
    {
        // Building the model does not connect, so a placeholder connection string is fine here.
        // A handful of persisters stand in for the full registered set (some need a live AssetSystem
        // or ProceduralMeshSystem to construct, which this schema-only test has no reason to spin up);
        // proving the persister-driven model wiring works generically for a couple of them is enough.
        var options = new DbContextOptionsBuilder<EditorDbContext>()
            .UseMySql("Server=localhost;Database=x;Uid=root", new MySqlServerVersion(new Version(8, 0, 0)))
            .Options;
        var persistence = new ISceneComponentPersistence[]
        {
            new MarkerComponentPersistence(null!),
            new StampComponentPersistence(null!),
        };
        using var context = new EditorDbContext(options, persistence, []);

        Schema schema = ModelSchema.Extract(context);

        Assert.IsTrue(schema.Tables.ContainsKey("scene_entities"), "model should define the generic scene entity table");
        Assert.IsTrue(schema.Tables.ContainsKey("scene_marker_components"), "model should define component tables");
        Assert.IsTrue(schema.Tables.ContainsKey("scene_stamp_components"), "model should define landscape stamp component table");
        SchemaTable entities = schema.Tables["scene_entities"];
        Assert.IsNotNull(entities.Column("MapId"));
        Assert.IsTrue(entities.Column("ParentId")?.Nullable == true, "ParentId should be nullable for root entities");
        Assert.IsTrue(entities.PrimaryKey.Contains("Id"));
        Assert.IsTrue(entities.Column("Id")!.AutoIncrement, "Id should be detected as auto-increment");
    }

    [EditorTest(Category = "Schema")]
    public static void Migration_sql_covers_common_changes()
    {
        var changes = new List<SchemaChange>
        {
            new(SchemaChangeKind.CreateTable, "things")
            {
                Definition = new SchemaTable("things",
                    [new SchemaColumn("Id", "int", false, true), new SchemaColumn("Name", "varchar(64)", true)],
                    ["Id"], []),
            },
            new(SchemaChangeKind.AddColumn, "a") { Column = new SchemaColumn("MapId", "int", false) },
            new(SchemaChangeKind.DropColumn, "a") { Column = new SchemaColumn("old", "int", true) },
            new(SchemaChangeKind.DropTable, "stray"),
        };

        string sql = MigrationSql.Generate(changes);

        Assert.IsTrue(sql.Contains("CREATE TABLE `things`"));
        Assert.IsTrue(sql.Contains("`Id` int NOT NULL AUTO_INCREMENT"));
        Assert.IsTrue(sql.Contains("PRIMARY KEY (`Id`)"));
        Assert.IsTrue(sql.Contains("ALTER TABLE `a` ADD COLUMN `MapId` int NOT NULL"));
        Assert.IsTrue(sql.Contains("ALTER TABLE `a` DROP COLUMN `old`"));
        Assert.IsTrue(sql.Contains("DROP TABLE `stray`"));
    }

    [EditorTest(Category = "Schema")]
    public static void Diff_detects_table_and_column_changes()
    {
        var expected = new Schema(
        [
            new SchemaTable("a", [Col("id"), Col("x")], ["id"], []),
            new SchemaTable("new_table", [Col("id")], ["id"], []),
        ]);

        var live = new Schema(
        [
            new SchemaTable("a", [Col("id"), Col("old")], ["id"], []),
            new SchemaTable("stray", [Col("id")], ["id"], []),
        ]);

        var changes = SchemaDiff.Compute(expected, live);

        Assert.IsTrue(changes.Any(c => c.Kind == SchemaChangeKind.CreateTable && c.Table == "new_table"));
        Assert.IsTrue(changes.Any(c => c.Kind == SchemaChangeKind.DropTable && c.Table == "stray"));
        Assert.IsTrue(changes.Any(c => c.Kind == SchemaChangeKind.AddColumn && c.Table == "a" && c.Column!.Name == "x"));
        Assert.IsTrue(changes.Any(c => c.Kind == SchemaChangeKind.DropColumn && c.Table == "a" && c.Column!.Name == "old"));
    }

    private static SchemaColumn Col(string name) => new(name, "int", false);
}
