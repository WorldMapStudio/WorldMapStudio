using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>An inclusive rectangular range of chunk coordinates — what a UV region converts into via
/// <see cref="PaintImage.ChunkRectForUv"/>, and what <see cref="ImageResidencySystem"/> unions across
/// every placement that references an image to build its residency target.</summary>
public readonly record struct ImageChunkRect(int MinX, int MinY, int MaxX, int MaxY)
{
    public IEnumerable<ImageChunkCoord> Coords()
    {
        for (int y = MinY; y <= MaxY; y++)
        {
            for (int x = MinX; x <= MaxX; x++)
            {
                yield return new ImageChunkCoord(x, y);
            }
        }
    }
}
