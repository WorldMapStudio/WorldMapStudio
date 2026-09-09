using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>Geometry read off the file(s) a user picked to back a disk <see cref="PaintImage"/> — what
/// the creation form fills its size / chunk size / component fields from.</summary>
public sealed record DiskImageSourceResult(
    string Path,
    string TilePattern,
    bool IsTiled,
    int Width,
    int Height,
    int ChunkSize,
    int Components,
    PaintImagePixelFormat Format);

/// <summary>
/// Inspects a picked image file, or a picked directory of <c>{x}_{y}</c> tiles, and works out the
/// canvas the resulting <see cref="PaintImage"/> should have. Pure filesystem access — the path is
/// whatever the native picker returned, absolute and unconstrained.
/// </summary>
public static class DiskImageProbe
{
    private const int MaxSingleImageSize = 4096;

    /// <summary>Treats <paramref name="path"/> as one image file backing a single-chunk image. Null if
    /// it cannot be read, or is larger than a single chunk can be.</summary>
    public static Task<DiskImageSourceResult?> ProbeFileAsync(string path) => Task.Run(() =>
    {
        if (!File.Exists(path) || Decode(path) is not { } image)
        {
            return null;
        }

        int longest = Math.Max(image.GetWidth(), image.GetHeight());
        if (longest is < 1 or > MaxSingleImageSize)
        {
            return null;
        }

        (int components, PaintImagePixelFormat format) = Describe(image, path);
        return new DiskImageSourceResult(
            path, "", IsTiled: false,
            image.GetWidth(), image.GetHeight(), Math.Max(16, longest), components, format);
    });

    /// <summary>Treats <paramref name="directory"/> as a folder of tiles named by
    /// <paramref name="tilePattern"/> (<c>{x}</c>/<c>{y}</c> tokens). Null if no tile matches.</summary>
    public static Task<DiskImageSourceResult?> ProbeFolderAsync(string directory, string tilePattern) => Task.Run(() =>
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        System.Text.RegularExpressions.Regex matcher = ImageDiskStore.TilePatternRegex(tilePattern);
        var coords = new List<ImageChunkCoord>();
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            System.Text.RegularExpressions.Match match = matcher.Match(Path.GetFileName(file));
            if (match.Success)
            {
                coords.Add(new ImageChunkCoord(
                    int.Parse(match.Groups["x"].Value),
                    int.Parse(match.Groups["y"].Value)));
            }
        }

        if (coords.Count == 0)
        {
            return null;
        }

        int maxX = coords.Max(c => c.X);
        int maxY = coords.Max(c => c.Y);

        if (ReadTile(directory, tilePattern, coords[0]) is not { } probe)
        {
            return null;
        }

        (int components, PaintImagePixelFormat format) = Describe(probe, tilePattern);
        int chunkSize = Math.Clamp(probe.GetWidth(), 16, MaxSingleImageSize);
        int lastColW = ReadTile(directory, tilePattern, new ImageChunkCoord(maxX, 0))?.GetWidth() ?? chunkSize;
        int lastRowH = ReadTile(directory, tilePattern, new ImageChunkCoord(0, maxY))?.GetHeight() ?? chunkSize;

        return new DiskImageSourceResult(
            directory, tilePattern, IsTiled: true,
            (maxX * chunkSize) + lastColW, (maxY * chunkSize) + lastRowH, chunkSize, components, format);
    });

    private static Image? ReadTile(string directory, string tilePattern, ImageChunkCoord coord)
    {
        string file = Path.Combine(directory, ImageDiskStore.TileFileName(tilePattern, coord));
        return File.Exists(file) ? Decode(file) : null;
    }

    private static Image? Decode(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            return null;
        }

        var image = new Image();
        Error error = AssetPath.Extension(path).ToLowerInvariant() == ".exr"
            ? image.LoadExrFromBuffer(bytes)
            : image.LoadPngFromBuffer(bytes);
        return error == Error.Ok ? image : null;
    }

    private static (int Components, PaintImagePixelFormat Format) Describe(Image image, string path)
    {
        if (AssetPath.Extension(path).ToLowerInvariant() == ".exr")
        {
            return (1, PaintImagePixelFormat.Float32);
        }

        int components = image.GetFormat() switch
        {
            Image.Format.L8 or Image.Format.R8 => 1,
            Image.Format.Rgb8 => 3,
            Image.Format.Rgba8 or Image.Format.La8 => 4,
            _ => image.DetectAlpha() != Image.AlphaMode.None ? 4 : 3,
        };

        return (components, PaintImagePixelFormat.Byte);
    }
}
