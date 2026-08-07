using System;
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
        var options = new DbContextOptionsBuilder<EditorDbContext>()
            .UseMySql("Server=localhost;Database=x;Uid=root", new MySqlServerVersion(new Version(8, 0, 0)))
            .Options;
        using var context = new EditorDbContext(options);

        Schema schema = ModelSchema.Extract(context);

        Assert.IsTrue(schema.Tables.ContainsKey("empties"), "model should define the empties table");
        SchemaTable empties = schema.Tables["empties"];
        Assert.IsNotNull(empties.Column("MapId"));
        Assert.IsTrue(empties.PrimaryKey.Contains("Id"));
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
