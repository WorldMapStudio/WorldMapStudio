using System.Linq;
using System.IO;

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
        project.AssetSources.Add(new AssetSourceSettings
        {
            Id = "loose",
            Name = "Loose Textures",
            Type = AssetSourceType.FileSystem,
            Enabled = true,
            RootPath = "D:\\Textures",
        });
        project.AssetSources.Add(new AssetSourceSettings
        {
            Id = "more",
            Name = "More Textures",
            Type = AssetSourceType.FileSystem,
            Enabled = false,
            RootPath = "E:\\Textures",
        });

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
            Assert.AreEqual(2, loaded.AssetSources.Count);
            Assert.AreEqual("loose", loaded.AssetSources[0].Id);
            Assert.AreEqual("Loose Textures", loaded.AssetSources[0].Name);
            Assert.AreEqual(AssetSourceType.FileSystem, loaded.AssetSources[1].Type);
            Assert.IsFalse(loaded.AssetSources[1].Enabled);
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

    [EditorTest(Category = "Project")]
    public static void Filesystem_asset_provider_lists_texture_assets()
    {
        string root = Path.Combine(Path.GetTempPath(), "__wms_assets_test__");
        string nested = Path.Combine(root, "Tiles");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(root, "grass.png"), "");
        File.WriteAllText(Path.Combine(nested, "stone.jpg"), "");
        File.WriteAllText(Path.Combine(root, "notes.txt"), "");

        try
        {
            var provider = new FileSystemAssetProvider(null!);
            var source = new AssetSourceSettings { Id = "textures", Name = "Textures", RootPath = root };

            var assets = provider.ListTextureAssets(source).OrderBy(asset => asset.Path).ToList();

            Assert.AreEqual(2, assets.Count);
            Assert.AreEqual("grass.png", assets[0].Path);
            Assert.AreEqual("textures::grass.png", assets[0].QualifiedPath);
            Assert.AreEqual(Path.Combine("Tiles", "stone.jpg"), assets[1].Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [EditorTest(Category = "Project")]
    public static void Asset_source_ids_are_path_safe_and_unique()
    {
        var sources = new[]
        {
            new AssetSourceSettings { Id = "textures" },
            new AssetSourceSettings { Id = "textures2" },
        };

        Assert.IsNull(AssetSourceId.Validate("terrain-textures", sources, null));
        Assert.IsNull(AssetSourceId.Validate("terrain.textures_01", sources, null));
        Assert.AreEqual("textures3", AssetSourceId.Unique("textures", sources.Select(source => source.Id)));
        Assert.IsNotNull(AssetSourceId.Validate("", sources, null));
        Assert.IsNotNull(AssetSourceId.Validate("terrain textures", sources, null));
        Assert.IsNotNull(AssetSourceId.Validate("textures", sources, null));
    }
}
