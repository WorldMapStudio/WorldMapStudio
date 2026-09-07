using System.Linq;
using System.Text.Json.Nodes;
using Godot;

namespace WorldMapStudio;

/// <summary>Covers export-profile persistence. Goes with the profile system.</summary>
public static class ExportProfileTests
{
    [EditorTest(Category = "Export")]
    public static void Export_profile_round_trips_through_disk()
    {
        var project = new Project { Name = "__wms_export_profile_test__" };
        var context = new EditorContext(new Node3D(), project);

        try
        {
            var settings = new JsonObject { ["folder"] = "exports/test" };
            ExportProfile created = context.Exports.Profiles.Create("My Profile", "wms.sample.alphamaps", settings);

            var reloaded = new ExportProfileRegistry(context);
            ExportProfile? loaded = reloaded.Profiles.FirstOrDefault(profile => profile.Id == created.Id);

            Assert.IsNotNull(loaded, "saved profile should load back");
            Assert.AreEqual("My Profile", loaded!.Name);
            Assert.AreEqual("wms.sample.alphamaps", loaded.ExporterId);
            Assert.AreEqual("exports/test", loaded.Settings["folder"]?.GetValue<string>());

            reloaded.Rename(loaded, "Renamed");
            var afterRename = new ExportProfileRegistry(context);
            Assert.AreEqual("Renamed", afterRename.Profiles.First(profile => profile.Id == created.Id).Name);

            afterRename.Delete(afterRename.Profiles.First(profile => profile.Id == created.Id));
            var afterDelete = new ExportProfileRegistry(context);
            Assert.IsFalse(afterDelete.Profiles.Any(profile => profile.Id == created.Id));
        }
        finally
        {
            ProjectStore.Delete(project);
        }
    }
}
