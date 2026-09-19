using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

public static class MigrationsScriptApiTests
{
    private const string ExtraColumn = "__wms_test_extra";

    [EditorTest(Category = "Migrations Script", Thread = TestThread.Background)]
    public static void Check_reports_a_column_added_behind_the_editors_back()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_migrations_script_test__" });
        var host = new ScriptEngineHost([context.Scripting.MigrationsScriptApi]);

        // Only a database this test can restore is touched, and only by adding a column of its own.
        Storage storage = context.Database.Storages.First(s => s.ExpectedSchema() is { Tables.Count: > 0 });
        string table = storage.ExpectedSchema()!.Tables.Keys.First();

        Assert.AreEqual("false", host.Evaluate("wms.migrations.Check().toString()"),
            "a database this editor just opened has nothing pending");
        Assert.AreEqual("0", host.Evaluate("wms.migrations.List().length.toString()"));

        BlockingWork.Run(() => storage.ApplySqlAsync($"ALTER TABLE `{table}` ADD COLUMN `{ExtraColumn}` INT NULL"));
        try
        {
            Assert.AreEqual("true", host.Evaluate("wms.migrations.Check().toString()"));
            Assert.AreEqual(storage.Name, host.Evaluate("wms.migrations.List()[0].Storage"));
            Assert.IsTrue(host.Evaluate("wms.migrations.List()[0].Changes[0]").Contains(ExtraColumn));
        }
        finally
        {
            BlockingWork.Run(() => storage.ApplySqlAsync($"ALTER TABLE `{table}` DROP COLUMN `{ExtraColumn}`"));
        }

        Assert.AreEqual("false", host.Evaluate("wms.migrations.Check().toString()"));
    }
}
