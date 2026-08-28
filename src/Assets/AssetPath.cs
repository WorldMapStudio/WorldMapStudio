using System;
using System.Collections.Generic;

namespace WorldMapStudio;

public static class AssetPath
{
    public static string RelativeTo(string basePath, string relative)
    {
        if (relative.Length == 0)
        {
            return relative;
        }

        string decoded = Uri.UnescapeDataString(Normalize(relative));
        if (decoded.Contains("://", StringComparison.Ordinal) || decoded.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return decoded;
        }

        if (decoded.StartsWith('/'))
        {
            return Collapse(decoded.TrimStart('/'));
        }

        string normalizedBase = Normalize(basePath);
        int slash = normalizedBase.LastIndexOf('/');
        string directory = slash >= 0 ? normalizedBase[..slash] : string.Empty;
        string combined = string.IsNullOrEmpty(directory) ? decoded : $"{directory}/{decoded}";
        return Collapse(combined);
    }

    public static string Normalize(string path) => path.Replace('\\', '/');

    public static string Extension(string path)
    {
        string normalized = TrimQueryOrFragment(Normalize(path));
        int slash = normalized.LastIndexOf('/');
        int dot = normalized.LastIndexOf('.');
        return dot > slash ? normalized[dot..] : string.Empty;
    }

    public static string FileName(string path)
    {
        string normalized = TrimQueryOrFragment(Normalize(path));
        int slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }

    private static string Collapse(string path)
    {
        List<string> parts = [];
        foreach (string part in Normalize(path).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }

                continue;
            }

            parts.Add(part);
        }

        return string.Join('/', parts);
    }

    private static string TrimQueryOrFragment(string path)
    {
        int marker = path.IndexOfAny(['?', '#']);
        return marker >= 0 ? path[..marker] : path;
    }
}
