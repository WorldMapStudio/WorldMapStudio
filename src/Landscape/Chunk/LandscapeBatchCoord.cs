namespace WorldMapStudio;

/// <summary>Which render batch a chunk falls in, at a given batch size. Rendering-only grouping:
/// nothing is stored per batch and chunks inside one are built exactly as they were.</summary>
public readonly record struct LandscapeBatchCoord(int X, int Y)
{
    public static LandscapeBatchCoord Of(ChunkCoord coord, int batchChunks) =>
        new(FloorDiv(coord.X, batchChunks), FloorDiv(coord.Y, batchChunks));

    /// <summary>The chunk at the batch's minimum corner.</summary>
    public ChunkCoord Origin(int batchChunks) => new(X * batchChunks, Y * batchChunks);

    /// <summary>Index of a chunk inside this batch, row-major, or -1 if it is not in it.</summary>
    public int IndexOf(ChunkCoord coord, int batchChunks)
    {
        if (!Of(coord, batchChunks).Equals(this))
        {
            return -1;
        }

        ChunkCoord origin = Origin(batchChunks);
        return ((coord.Y - origin.Y) * batchChunks) + (coord.X - origin.X);
    }

    /// <summary>Identity streaming deduplicates on. Mirrors <see cref="ChunkCoord.KeyFor"/>: 24 bits
    /// of map id, 20 bits each of X and Y.</summary>
    public long KeyFor(MapId map) =>
        ((long)(map.Value & 0xFFFFFF) << 40) |
        ((long)(X & 0xFFFFF) << 20) |
        (uint)(Y & 0xFFFFF);

    // Chunk coords are signed and routinely negative; a plain '/' would put chunk -1 and chunk 0 in
    // one batch. Same negative-safe semantics as LandscapeChannelPool.FloorDiv.
    private static int FloorDiv(int value, int divisor) =>
        value >= 0 ? value / divisor : ((value + 1) / divisor) - 1;
}
