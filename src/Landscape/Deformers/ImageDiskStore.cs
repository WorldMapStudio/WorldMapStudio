using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// Reads and writes a disk-backed <see cref="PaintImage"/>'s chunks as image files on the filesystem —
/// the <see cref="PaintImageStorageKind.Disk"/> counterpart to the <c>wms_image_chunks</c> table. The
/// image's <see cref="PaintImage.DiskPath"/> is an absolute path chosen through the OS file picker: the
/// image file itself for a single-chunk grid, the directory holding the tiles otherwise (named by
/// <see cref="PaintImage.DiskTilePattern"/>).
///
/// The shape mirrors <see cref="EditorStorage.LoadImageChunksAsync"/> and
/// <see cref="PaintImageFactory.Stage"/> so the two storage kinds stay interchangeable above this line:
/// a coord whose file is missing simply does not come back (read as all-zero, like an absent row).
/// </summary>
public sealed class ImageDiskStore
{
    /// <summary>Which chunk coordinates currently have a file on disk — the disk equivalent of the
    /// <c>image_chunks</c> coordinate manifest a catalog load reads.</summary>
    public Task<IReadOnlyList<ImageChunkCoord>> ListManifestAsync(PaintImage image) => Task.Run<IReadOnlyList<ImageChunkCoord>>(() =>
    {
        if (!image.IsTiledDisk)
        {
            return File.Exists(image.DiskChunkPath(new ImageChunkCoord(0, 0))) ? [new ImageChunkCoord(0, 0)] : [];
        }

        if (!Directory.Exists(image.DiskPath))
        {
            return [];
        }

        Regex matcher = TilePatternRegex(image.DiskTilePattern);
        var coords = new List<ImageChunkCoord>();
        foreach (string file in Directory.EnumerateFiles(image.DiskPath))
        {
            Match match = matcher.Match(Path.GetFileName(file));
            if (!match.Success)
            {
                continue;
            }

            int x = int.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture);
            int y = int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
            if (x >= 0 && x < image.ChunksX && y >= 0 && y < image.ChunksY)
            {
                coords.Add(new ImageChunkCoord(x, y));
            }
        }

        return coords;
    });

    /// <summary>Decoded pixel buffers for the wanted coords whose file exists. A coord with no file is
    /// left out, matching the sampler's "absent means zero" contract.</summary>
    public Task<IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)>> ReadChunksAsync(
        PaintImage image, IReadOnlyCollection<ImageChunkCoord> coords) => Task.Run<IReadOnlyList<(ImageChunkCoord, byte[])>>(() =>
    {
        var result = new List<(ImageChunkCoord, byte[])>();
        foreach (ImageChunkCoord coord in coords)
        {
            string path = image.DiskChunkPath(coord);
            if (!File.Exists(path))
            {
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (IOException)
            {
                continue;
            }

            string ext = AssetPath.Extension(path);
            byte[] tile = ImageDiskCodec.Decode(bytes, ext.Length == 0 ? image.DiskExtension : ext, image.ChunkSize, image.Stride, image.Components, image.Format);
            result.Add((coord, tile));
        }

        return result;
    });

    /// <summary>Writes the given chunk buffers to disk and deletes the files for emptied coords. Called
    /// from the commit write-back — see <see cref="PaintImageFactory.Stage"/>.</summary>
    public Task WriteChunksAsync(
        PaintImage image,
        IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)> upserts,
        IReadOnlyList<ImageChunkCoord> deletes) => Task.Run(() =>
    {
        foreach ((ImageChunkCoord coord, byte[] pixels) in upserts)
        {
            (int fillW, int fillH) = FillSize(image, coord);
            byte[] file = ImageDiskCodec.Encode(pixels, image.ChunkSize, image.Stride, image.Components, image.Format, fillW, fillH);
            string path = image.DiskChunkPath(coord);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, file);
        }

        // A single-chunk image is one file — never deleted out from under itself, just left holding
        // whatever the last non-empty state wrote. Only a tiled image drops individual tile files.
        if (!image.IsTiledDisk)
        {
            return;
        }

        foreach (ImageChunkCoord coord in deletes)
        {
            string path = image.DiskChunkPath(coord);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    });

    /// <summary>The live (canvas-clipped) pixel extent of one chunk — a full tile except in the last
    /// column/row when <see cref="PaintImage.ChunkSize"/> does not divide the canvas evenly.</summary>
    private static (int Width, int Height) FillSize(PaintImage image, ImageChunkCoord coord)
    {
        int w = Math.Min(image.ChunkSize, image.Width - (coord.X * image.ChunkSize));
        int h = Math.Min(image.ChunkSize, image.Height - (coord.Y * image.ChunkSize));
        return (Math.Max(1, w), Math.Max(1, h));
    }

    /// <summary>One tile's file name from a <c>{x}</c>/<c>{y}</c> pattern.</summary>
    public static string TileFileName(string pattern, ImageChunkCoord coord) =>
        (string.IsNullOrWhiteSpace(pattern) ? PaintImage.DefaultDiskTilePattern : pattern)
            .Replace("{x}", coord.X.ToString(CultureInfo.InvariantCulture))
            .Replace("{y}", coord.Y.ToString(CultureInfo.InvariantCulture));

    /// <summary>Turns a <c>{x}</c>/<c>{y}</c> tile pattern into a regex with named <c>x</c>/<c>y</c>
    /// integer groups, matching one file name (not a path).</summary>
    public static Regex TilePatternRegex(string pattern)
    {
        string escaped = Regex.Escape(string.IsNullOrWhiteSpace(pattern) ? PaintImage.DefaultDiskTilePattern : pattern)
            .Replace("\\{x}", "(?<x>-?\\d+)")
            .Replace("\\{y}", "(?<y>-?\\d+)");
        return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
