using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// An immutable snapshot of a chunked <see cref="PaintImage"/>'s chunk buffers — coord to pixel
/// buffer. A coord absent from the table means that chunk is all-zero; there is (for now) no
/// distinction between "empty" and "stored but not loaded" — that arrives with residency tracking.
///
/// Snapshotting the coord-to-buffer mapping (a shallow copy, sized by chunk count rather than pixel
/// count) is what lets <see cref="PaintImage.CreateSampler"/> hand a landscape build a view that
/// cannot have chunks appear or disappear underneath it mid-build, matching the snapshot-on-the-main-
/// thread pattern <see cref="LandscapeBatchLoader.Prepare"/> already uses for everything else a build
/// reads. The buffers themselves can still be edited in place during a stroke — with the same tearing
/// tolerance as any in-place raster edit, but the chunk set itself never moves under a reader.
/// </summary>
public sealed class ImageChunkTable
{
    public static readonly ImageChunkTable Empty = new(new Dictionary<ImageChunkCoord, ImageChunk>());

    private readonly IReadOnlyDictionary<ImageChunkCoord, ImageChunk> _chunks;

    public ImageChunkTable(IReadOnlyDictionary<ImageChunkCoord, ImageChunk> chunks)
    {
        _chunks = chunks;
    }

    public IEnumerable<ImageChunkCoord> Coords => _chunks.Keys;

    public int Count => _chunks.Count;

    public bool TryGet(ImageChunkCoord coord, out ImageChunk? chunk) => _chunks.TryGetValue(coord, out chunk!);
}
