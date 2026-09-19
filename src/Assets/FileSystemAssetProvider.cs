using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>Basic disk-backed asset source for loose files outside Godot's resource tree.</summary>
[Subsystem(nameof(AssetSystem))]
public sealed class FileSystemAssetProvider : IAssetProvider
{
    public FileSystemAssetProvider(AssetSystem assets)
    {
    }

    public bool Supports(string type) => type == AssetSourceType.FileSystem;

    public IEnumerable<AssetRef> ListAssets(AssetSourceSettings source)
    {
        if (!Directory.Exists(source.RootPath))
        {
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(source.RootPath, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source.RootPath, file);
            yield return new AssetRef(AssetKind.Unknown, source.Id, source.Name, relative, Path.GetFileName(file), file);
        }
    }

    public async Task<byte[]?> ReadBytesAsync(AssetSourceSettings source, string path)
    {
        if (!TryResolve(source, path, out string fullPath))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(fullPath).ConfigureAwait(false);
    }

    public async Task<string?> ReadTextAsync(AssetSourceSettings source, string path)
    {
        if (!TryResolve(source, path, out string fullPath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(fullPath).ConfigureAwait(false);
    }

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
