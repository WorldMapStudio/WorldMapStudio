using System.Linq;

namespace WorldMapStudio;

/// <summary>Covers project-level settings behaviour.</summary>
public static class ProjectTests
{
    [EditorTest(Category = "Project")]
    public static void Project_round_trips_through_disk()
    {
        var project = new Project
        {
            Name = "__wms_test_roundtrip__",
            AxisConvention = AxisConvention.Create(SignedAxis.PosZ, SignedAxis.PosX, SignedAxis.NegY),
        };
        project.GetOrAddStorageConnection("Editor", new StorageConnection { Database = "editor", Port = 3399, LaunchServer = true });

        try
        {
            ProjectStore.Save(project);
            Project? loaded = ProjectStore.LoadAll().FirstOrDefault(p => p.Name == project.Name);

            Assert.IsNotNull(loaded, "saved project should load back");
            Assert.AreEqual(SignedAxis.PosZ, loaded!.AxisConvention.X);
            Assert.AreEqual(SignedAxis.NegY, loaded.AxisConvention.Z);
            Assert.IsTrue(loaded.StorageConnections.ContainsKey("Editor"));
            Assert.AreEqual(3399, loaded.StorageConnections["Editor"].Port);
            Assert.IsTrue(loaded.StorageConnections["Editor"].LaunchServer);
        }
        finally
        {
            ProjectStore.Delete(project);
        }
    }

    [EditorTest(Category = "Project")]
    public static void Storage_connection_is_added_once_and_reused()
    {
        var project = new Project { Name = "Test" };

        StorageConnection first = project.GetOrAddStorageConnection("Editor", new StorageConnection { Database = "editor" });
        Assert.AreEqual("editor", first.Database);

        // A later call must return the stored connection, not overwrite a user's edits with defaults.
        first.Port = 3399;
        StorageConnection second = project.GetOrAddStorageConnection("Editor", new StorageConnection { Port = 3312 });
        Assert.AreEqual(3399, second.Port);
        Assert.IsTrue(ReferenceEquals(first, second));
    }
}
