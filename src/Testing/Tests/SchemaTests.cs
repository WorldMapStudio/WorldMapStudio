using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace WorldMapStudio;

/// <summary>Covers schema introspection and diffing used by the migration flow.</summary>
public static class SchemaTests
{
    [EditorTest(Category = "Schema")]
    public static void Model_schema_matches_the_editor_context()
    {
        // Building the model does not connect, so a placeholder connection string is fine here.
        // A handful of persisters/factories stand in for the full registered set (some need a live
        // AssetSystem or ProceduralSystem to construct, which this schema-only test has no reason to
        // spin up); proving the self-registered-config wiring works generically for a couple is enough.
        var options = new DbContextOptionsBuilder<EditorDbContext>()
            .UseMySql("Server=localhost;Database=x;Uid=root", new MySqlServerVersion(new Version(8, 0, 0)))
            .Options;
        var persistence = new ISceneComponentPersistence[]
        {
            new MarkerComponentPersistence(null!),
            new StampComponentPersistence(null!),
        };
        var entityFactories = new IEntityFactory[] { new MapSceneEntityFactory(null!) };
        var tables = new ITableConfiguration[] { new EntityTableConfiguration(null!) };
        using var context = new EditorDbContext(options, persistence, entityFactories, tables);

        Schema schema = ModelSchema.Extract(context);

        Assert.IsTrue(schema.Tables.ContainsKey("wms_entities"), "model should define the entity identity table");
        Assert.IsTrue(schema.Tables.ContainsKey("wms_map_entities"), "model should define the editor-authored entity table");
        Assert.IsTrue(schema.Tables.ContainsKey("wms_scene_marker_components"), "model should define component tables");
        Assert.IsTrue(schema.Tables.ContainsKey("wms_scene_stamp_components"), "model should define landscape stamp component table");
        SchemaTable entities = schema.Tables["wms_map_entities"];
        Assert.IsNotNull(entities.Column("MapId"));
        Assert.IsTrue(entities.PrimaryKey.Contains("Id"));
        // Id is ValueGeneratedNever(): MapSceneEntityFactory assigns it client-side (a MAX(Id)-seeded
        // high-water mark) so Pomelo can batch inserts, even though the live column stays AUTO_INCREMENT.
        Assert.IsFalse(entities.Column("Id")!.AutoIncrement, "Id is client-assigned, not database-generated");
        Assert.IsTrue(
            entities.Indexes.Any(index => index.Columns.SequenceEqual(["MapId", "MinX", "MaxX", "MinY", "MaxY"])),
            "the streaming region query needs a bounds index");

        IForeignKey identity = context.Model.FindEntityType(typeof(MapEntityRecord))!.GetForeignKeys().Single();
        Assert.AreEqual(typeof(EntityRecord), identity.PrincipalEntityType.ClrType);
        Assert.AreEqual(DeleteBehavior.Cascade, identity.DeleteBehavior);

        SchemaTable identities = schema.Tables["wms_entities"];
        Assert.IsTrue(identities.Column("Source")?.Nullable == true, "Source is null for a native entity");
        Assert.IsTrue(
            identities.Indexes.Any(index => index.Unique && index.Columns.SequenceEqual(["Source", "SourceKey"])),
            "a bridged entity's source row is identified once");
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
