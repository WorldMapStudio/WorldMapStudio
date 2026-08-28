using System;
using System.IO;

namespace WorldMapStudio;

public static class AssetPath
{
    public static string RelativeTo(string basePath, string relative)
    {
        if (relative.Length == 0)
        {
            return relative;
        }

        string decoded = Uri.UnescapeDataString(relative.Replace('\\', '/'));
        if (decoded.Contains("://", StringComparison.Ordinal) || decoded.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return decoded;
        }

        string? directory = Path.GetDirectoryName(basePath);
        string combined = string.IsNullOrEmpty(directory)
            ? decoded
            : Path.Combine(directory, decoded);
        return Normalize(combined);
    }

    public static string Normalize(string path) => path.Replace('\\', '/');
}
