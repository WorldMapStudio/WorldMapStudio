namespace WorldMapStudio;

/// <summary>
/// The render unit for terrain: one mesh, one material and one alpha texture for a square of chunks.
/// The chunk stays the data/authoring/export unit; a batch is only how chunks are grouped for
/// drawing, driven by a view setting.
/// </summary>
public sealed partial class LandscapeTerrainBatch
{
    /// <summary>Upper bound on the batch-size view setting.</summary>
    public const int MaxTerrainBatchChunks = 16;

    /// <summary>The shader's compile-time slot loop bound.</summary>
    public const int MaxSlots = 16;
}
