using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// Reads and writes a disk-backed <see cref="PaintImage"/>'s chunks as image files under its configured
/// asset source — the <see cref="PaintImageStorageKind.Disk"/> counterpart to the <c>wms_image_chunks</c>
/// table. A single-chunk image is one file at <see cref="PaintImage.DiskPath"/>; a larger one is a
/// directory of tiles named by <see cref="PaintImage.DiskTilePattern"/>.
///
/// The shape mirrors <see cref="EditorStorage.LoadImageChunksAsync"/> and
/// <see cref="PaintImageFactory.Stage"/> so the two storage kinds stay interchangeable above this line:
/// a coord whose file is missing simply does not come back (read as all-zero, like an absent row).
/// </summary>
public sealed class ImageDiskStore
{
    private readonly AssetSystem _assets;

    public ImageDiskStore(AssetSystem assets)
    {
        _assets = assets;
    }

    /// <summary>Which chunk coordinates currently have a file on disk — the disk equivalent of the
    /// <c>image_chunks</c> coordinate manifest a catalog load reads.</summary>
    public async Task<IReadOnlyList<ImageChunkCoord>> ListManifestAsync(PaintImage image)
    {
        if (!image.IsTiledDisk)
        {
            byte[]? single = await _assets.ReadAssetBytesFromAsync(image.DiskSourceId, image.DiskChunkPath(new ImageChunkCoord(0, 0))).ConfigureAwait(false);
            return single is { Length: > 0 } ? [new ImageChunkCoord(0, 0)] : [];
        }

        string directory = image.DiskPath;
        IReadOnlyList<string> paths = await _assets.ListSourcePathsAsync(image.DiskSourceId, directory).ConfigureAwait(false);
        Regex matcher = TilePatternRegex(image.DiskTilePattern);

        var coords = new List<ImageChunkCoord>();
        foreach (string path in paths)
        {
            string file = AssetPath.FileName(path);
            Match match = matcher.Match(file);
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
    }

    /// <summary>Decoded pixel buffers for the wanted coords whose file exists. A coord with no file is
    /// left out, matching the sampler's "absent means zero" contract.</summary>
    public async Task<IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)>> ReadChunksAsync(
        PaintImage image, IReadOnlyCollection<ImageChunkCoord> coords)
    {
        var result = new List<(ImageChunkCoord, byte[])>();
        foreach (ImageChunkCoord coord in coords)
        {
            string path = image.DiskChunkPath(coord);
            byte[]? bytes = await _assets.ReadAssetBytesFromAsync(image.DiskSourceId, path).ConfigureAwait(false);
            if (bytes is not { Length: > 0 })
            {
                continue;
            }

            string ext = AssetPath.Extension(path);
            byte[] tile = ImageDiskCodec.Decode(bytes, ext.Length == 0 ? image.DiskExtension : ext, image.ChunkSize, image.Stride, image.Components, image.Format);
            result.Add((coord, tile));
        }

        return result;
    }

    /// <summary>Writes the given chunk buffers to disk and deletes the files for emptied coords. Called
    /// from the commit write-back — see <see cref="PaintImageFactory.Stage"/>.</summary>
    public async Task WriteChunksAsync(
        PaintImage image,
        IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)> upserts,
        IReadOnlyList<ImageChunkCoord> deletes)
    {
        foreach ((ImageChunkCoord coord, byte[] pixels) in upserts)
        {
            (int fillW, int fillH) = FillSize(image, coord);
            byte[] file = ImageDiskCodec.Encode(pixels, image.ChunkSize, image.Stride, image.Components, image.Format, fillW, fillH);
            await _assets.WriteAssetBytesAsync(image.DiskSourceId, image.DiskChunkPath(coord), file).ConfigureAwait(false);
        }

        // A single-chunk image is one file — never deleted out from under itself, just left holding
        // whatever the last non-empty state wrote. Only a tiled image drops individual tile files.
        if (image.IsTiledDisk)
        {
            foreach (ImageChunkCoord coord in deletes)
            {
                await _assets.DeleteAssetAsync(image.DiskSourceId, image.DiskChunkPath(coord)).ConfigureAwait(false);
            }
        }
    }

    /// <summary>The live (canvas-clipped) pixel extent of one chunk — a full tile except in the last
    /// column/row when <see cref="PaintImage.ChunkSize"/> does not divide the canvas evenly.</summary>
    private static (int Width, int Height) FillSize(PaintImage image, ImageChunkCoord coord)
    {
        int w = Math.Min(image.ChunkSize, image.Width - (coord.X * image.ChunkSize));
        int h = Math.Min(image.ChunkSize, image.Height - (coord.Y * image.ChunkSize));
        return (Math.Max(1, w), Math.Max(1, h));
    }

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
