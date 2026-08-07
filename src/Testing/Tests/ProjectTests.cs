namespace WorldMapStudio;

/// <summary>Covers project-level settings behaviour.</summary>
public static class ProjectTests
{
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
