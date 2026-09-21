using System.IO;
using System.Text;

namespace WorldMapStudio;

/// <summary>Covers <see cref="FileSystemAssetProvider"/> folding requested paths for lowercase trees.</summary>
public static class FileSystemAssetProviderTests
{
    [EditorTest(Category = "Assets", Thread = TestThread.Background)]
    public static void Lowercase_source_folds_relative_paths_but_not_the_root()
    {
        string root = Path.Combine(Path.GetTempPath(), "__wms_lowercase_paths_Root__");
        string directory = Path.Combine(root, "sub");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "file.txt"), "content");

        try
        {
            var source = new AssetSourceSettings { Id = "lower", RootPath = root };
            source.SetFlag(FileSystemAssetProvider.LowercasePathsKey, true);
            var provider = new FileSystemAssetProvider(null!);

            byte[]? bytes = provider.ReadBytesAsync(source, "Sub\\File.TXT".Replace('\\', Path.DirectorySeparatorChar)).GetAwaiter().GetResult();

            Assert.IsNotNull(bytes, "a mixed-case request resolves in a lowercase tree");
            Assert.AreEqual("content", Encoding.UTF8.GetString(bytes!));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [EditorTest(Category = "Assets", Thread = TestThread.Background)]
    public static void Lowercase_flag_reads_a_boolean_from_the_source_json()
    {
        var source = System.Text.Json.JsonSerializer.Deserialize<AssetSourceSettings>(
            "{\"id\":\"wow\",\"rootPath\":\"x\",\"lowercasePaths\":true}",
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.IsNotNull(source);
        Assert.IsTrue(source!.GetFlag(FileSystemAssetProvider.LowercasePathsKey), "a JSON boolean counts as the flag");

        source.SetFlag(FileSystemAssetProvider.LowercasePathsKey, false);
        Assert.IsFalse(source.GetFlag(FileSystemAssetProvider.LowercasePathsKey), "clearing the flag overrides the JSON value");
    }
}
