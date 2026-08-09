
using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// One resolved texture slot's output: the material bound to it, and the alpha coverage for it. The
/// base slot carries no alpha — it is opaque underneath everything.
/// </summary>
public sealed class LandscapeChunkLayer
{
    public required LandscapeMaterial? Material { get; init; }

    /// <summary>Coverage at the map's alpha resolution, row-major, or null for the base slot.</summary>
    public required byte[]? Alpha { get; init; }

    public bool IsBase => Alpha == null;
}

/// <summary>
/// What a chunk resolved and evaluated to: a heightmap and the texture slots over it. This is the
/// whole of what rendering needs, and — when a persistent cache eventually lands — the whole of what
/// an exporter would read.
/// </summary>
public sealed class LandscapeChunkOutput
{
    public required ChunkCoord Coord { get; init; }

    /// <summary>Vertices along a chunk edge; the heightmap is this squared.</summary>
    public required int HeightResolution { get; init; }

    /// <summary>Row-major heights in world units, <see cref="HeightResolution"/> squared.</summary>
    public required float[] Heights { get; init; }

    /// <summary>Alpha texels along a chunk edge.</summary>
    public required int AlphaResolution { get; init; }

    /// <summary>Slots in compositing order; the first is the base when one exists.</summary>
    public required IReadOnlyList<LandscapeChunkLayer> Layers { get; init; }

    public float HeightAt(int x, int y) => Heights[(y * HeightResolution) + x];
}
