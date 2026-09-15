using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WorldMapStudio;

public sealed class SystemFontFamilyInfo
{
    public required string Family { get; init; }
    public bool Supported { get; init; }
    public string? UnsupportedReason { get; init; }
}

public sealed class SystemFontVariant
{
    public required int Weight { get; init; }
    public required bool Italic { get; init; }
    public required string Path { get; init; }
    public int FaceIndex { get; init; }
}

/// <summary>
/// System font enumeration, backed entirely by Godot's <c>OS</c> font service (DirectWrite on
/// Windows, fontconfig on Linux, CoreText on macOS) — no platform-specific code of our own. Families
/// are scanned once per session on a background <see cref="WorkQueue"/> task; each family's weight/
/// italic variants are probed lazily, the first time that family is selected in the picker.
/// </summary>
public static class SystemFontCatalog
{
    private static readonly object Lock = new();
    private static readonly Dictionary<string, IReadOnlyList<SystemFontVariant>> VariantCache = new(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<SystemFontFamilyInfo>? _families;
    private static WorkHandle? _scanHandle;

    /// <summary>Null until the first scan finishes. Check <see cref="IsScanning"/> to show a spinner
    /// meanwhile.</summary>
    public static IReadOnlyList<SystemFontFamilyInfo>? Families
    {
        get
        {
            lock (Lock)
            {
                return _families;
            }
        }
    }

    public static bool IsScanning
    {
        get
        {
            lock (Lock)
            {
                return _families is null && _scanHandle is not null;
            }
        }
    }

    /// <summary>Starts the background scan if it hasn't already run this session. Safe to call every
    /// frame the Fonts tab is open.</summary>
    public static void EnsureScanStarted()
    {
        lock (Lock)
        {
            if (_families is not null || _scanHandle is not null)
            {
                return;
            }

            _scanHandle = WorkQueue.Schedule("Scan system fonts", _ =>
            {
                IReadOnlyList<SystemFontFamilyInfo> result = ScanFamilies();
                lock (Lock)
                {
                    _families = result;
                }
            });
        }
    }

    public static void Refresh()
    {
        lock (Lock)
        {
            _families = null;
            _scanHandle = null;
            VariantCache.Clear();
        }

        EnsureScanStarted();
    }

    /// <summary>Weight/italic variants available for <paramref name="family"/>, probing
    /// <c>OS.GetSystemFontPath</c> once per weight × italic and deduplicating by the path it returns
    /// (a family with only Regular/Bold maps every intermediate weight onto one of those two files).
    /// Cached after the first call.</summary>
    public static IReadOnlyList<SystemFontVariant> Variants(string family)
    {
        lock (Lock)
        {
            if (VariantCache.TryGetValue(family, out IReadOnlyList<SystemFontVariant>? cached))
            {
                return cached;
            }
        }

        List<SystemFontVariant> variants = ProbeVariants(family);
        lock (Lock)
        {
            VariantCache[family] = variants;
        }

        return variants;
    }

    private static IReadOnlyList<SystemFontFamilyInfo> ScanFamilies()
    {
        string[] names;
        try
        {
            names = Godot.OS.GetSystemFonts();
        }
        catch (Exception)
        {
            return [];
        }

        List<SystemFontFamilyInfo> result = new(names.Length);
        foreach (string family in names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static n => n, StringComparer.OrdinalIgnoreCase))
        {
            string? path = SafeGetSystemFontPath(family, 400, 100, false);
            (bool supported, string? reason) = path is null
                ? (false, "No usable font file was returned for this family.")
                : Classify(path);
            result.Add(new SystemFontFamilyInfo { Family = family, Supported = supported, UnsupportedReason = reason });
        }

        return result;
    }

    private static List<SystemFontVariant> ProbeVariants(string family)
    {
        List<SystemFontVariant> variants = [];
        foreach (bool italic in new[] { false, true })
        {
            HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);
            for (int weight = 100; weight <= 900; weight += 100)
            {
                string? path = SafeGetSystemFontPath(family, weight, 100, italic);
                if (path is null || !seenPaths.Add(path))
                {
                    continue;
                }

                int faceIndex = ResolveFaceIndex(path, family, italic);
                variants.Add(new SystemFontVariant { Weight = weight, Italic = italic, Path = path, FaceIndex = faceIndex });
            }
        }

        return variants;
    }

    /// <summary>Which face inside a <c>.ttc</c> collection matches <paramref name="requestedFamily"/>
    /// — Godot's font service hands back a collection path with no face index of its own. Anything
    /// else (a plain <c>.ttf</c>/<c>.otf</c>) is always face 0.</summary>
    private static int ResolveFaceIndex(string path, string requestedFamily, bool italic)
    {
        if (!path.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        byte[]? data = SafeReadAllBytes(path);
        if (data is null)
        {
            return 0;
        }

        try
        {
            int faceCount = TrueTypeFontReader.FaceCount(data);
            int fallback = 0;
            bool foundFamily = false;

            for (int face = 0; face < faceCount; face++)
            {
                if (TrueTypeFontReader.ReadFaceNames(data, face) is not { } names)
                {
                    continue;
                }

                string faceFamily = names.TypographicFamily ?? names.Family ?? string.Empty;
                if (!faceFamily.Equals(requestedFamily, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!foundFamily)
                {
                    fallback = face;
                    foundFamily = true;
                }

                string subfamily = names.TypographicSubfamily ?? names.Subfamily ?? string.Empty;
                bool faceItalic = subfamily.Contains("Italic", StringComparison.OrdinalIgnoreCase)
                    || subfamily.Contains("Oblique", StringComparison.OrdinalIgnoreCase);

                if (faceItalic == italic)
                {
                    return face;
                }
            }

            return fallback;
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return 0;
        }
    }

    private static (bool Supported, string? Reason) Classify(string path)
    {
        byte[]? header = ReadHeader(path, 4);
        if (header is null)
        {
            return (false, "Could not read the font file.");
        }

        uint tag = TrueTypeFontReader.ReadTag(header);
        return tag switch
        {
            TrueTypeFontReader.SfntTrueType or TrueTypeFontReader.SfntTrue => (true, null),
            TrueTypeFontReader.SfntCollection => (true, null),
            TrueTypeFontReader.SfntOtto =>
                (false, "CFF-outline .otf fonts aren't rasterized by this build's bundled stb_truetype."),
            _ => (false, "Unrecognized font file header."),
        };
    }

    private static string? SafeGetSystemFontPath(string family, int weight, int stretch, bool italic)
    {
        try
        {
            string path = Godot.OS.GetSystemFontPath(family, weight, stretch, italic);
            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[]? ReadHeader(string path, int count)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            byte[] buffer = new byte[count];
            return stream.Read(buffer, 0, count) == count ? buffer : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static byte[]? SafeReadAllBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
