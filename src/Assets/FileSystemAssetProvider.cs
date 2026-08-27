using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>Basic disk-backed asset source for loose files outside Godot's resource tree.</summary>
[Subsystem(nameof(AssetSystem))]
public sealed class FileSystemAssetProvider : IAssetProvider
{
    private static readonly HashSet<string> TextureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp",
        ".exr",
        ".hdr",
        ".jpg",
        ".jpeg",
        ".png",
        ".svg",
        ".tga",
        ".webp",
    };

    public FileSystemAssetProvider(AssetSystem assets)
    {
    }

    public float Priority => 0f;

    public bool Supports(AssetSourceType type) => type == AssetSourceType.FileSystem;

    public IEnumerable<AssetRef> ListTextureAssets(AssetSourceSettings source)
    {
        if (!Directory.Exists(source.RootPath))
        {
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(source.RootPath, "*", SearchOption.AllDirectories))
        {
            if (!TextureExtensions.Contains(Path.GetExtension(file)))
            {
                continue;
            }

            string relative = Path.GetRelativePath(source.RootPath, file);
            yield return new AssetRef(AssetKind.Texture, source.Id, source.Name, relative, Path.GetFileName(file), file);
        }
    }

    public Task<Image?> LoadTextureImageAsync(AssetSourceSettings source, string path)
    {
        if (!TryResolve(source, path, out string fullPath))
        {
            return Task.FromResult<Image?>(null);
        }

        var image = new Image();
        if (image.Load(fullPath) != Error.Ok)
        {
            return Task.FromResult<Image?>(null);
        }

        return Task.FromResult<Image?>(image);
    }

    public Texture2D? LoadTextureAsset(AssetSourceSettings source, string path) =>
        LoadTextureImageAsync(source, path).GetAwaiter().GetResult() is { } image
            ? ImageTexture.CreateFromImage(image)
            : null;

    private static bool TryResolve(AssetSourceSettings source, string path, out string fullPath)
    {
        fullPath = "";
        if (!Directory.Exists(source.RootPath) || path.Length == 0)
        {
            return false;
        }

        string candidate = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(source.RootPath, path));

        string root = Path.GetFullPath(source.RootPath);
        string relative;
        try
        {
            relative = Path.GetRelativePath(root, candidate);
        }
        catch (ArgumentException)
        {
            return false;
        }

        bool escapesRoot = relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
        if (escapesRoot || Path.IsPathRooted(relative) || !File.Exists(candidate))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }
}
