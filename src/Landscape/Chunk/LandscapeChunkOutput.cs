
using System.Collections.Generic;
using Godot;

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

    /// <summary>
    /// Hole cells along a chunk edge. Independent of <see cref="HeightResolution"/> and
    /// <see cref="AlphaResolution"/> — see <see cref="LandscapeSettings.ChunkHoleResolution"/>.
    /// </summary>
    public required int HoleResolution { get; init; }

    /// <summary>Row-major hole flags, <see cref="HoleResolution"/> squared. True cuts the cell out of the mesh.</summary>
    public required bool[] Holes { get; init; }

    /// <summary>
    /// Row-major vertex color, <see cref="HeightResolution"/> squared — the same grid as
    /// <see cref="Heights"/>, so it maps onto the mesh's vertex buffer index-for-index. Starts white
    /// each build; consumed by the shader as a multiplier over the splatted albedo.
    /// </summary>
    public required Color[] VertexColors { get; init; }

    /// <summary>
    /// Row-major vertex light, <see cref="HeightResolution"/> squared — same grid as
    /// <see cref="VertexColors"/>. Starts black each build. Additive <b>linear</b> light reaching the
    /// surface, in the same units as the scene's ambient and direct light — 1.0 is roughly one unit of
    /// full sunlight. Modulated by albedo and not attenuated by the scene's own lighting, so it stays
    /// visible at night.
    /// </summary>
    public required Color[] VertexLight { get; init; }

    /// <summary>
    /// Per-chunk terrain attribute values, keyed by <see cref="TerrainAttribute.Key"/>. An attribute
    /// no surviving claim wrote is absent — read it as its declared default. Not <c>required</c>: only
    /// an exporter and the debug overlay consume it, and the many test construction sites should not
    /// have to name it.
    /// </summary>
    public IReadOnlyDictionary<string, TerrainAttributeGrid> Attributes { get; init; } =
        new Dictionary<string, TerrainAttributeGrid>();

    /// <summary>
    /// Row-major heights of the cell-centre vertices, <c>(HeightResolution - 1)</c> squared, or null
    /// when the layout has none (<see cref="HeightVertexLayout.Grid"/>). Kept apart from
    /// <see cref="Heights"/> so everything reading the corner grid is unaffected.
    /// </summary>
    public float[]? CentreHeights { get; init; }

    /// <summary>Cell-centre counterpart of <see cref="VertexColors"/>, same shape as <see cref="CentreHeights"/>.</summary>
    public Color[]? CentreVertexColors { get; init; }

    /// <summary>Cell-centre counterpart of <see cref="VertexLight"/>, same shape as <see cref="CentreHeights"/>.</summary>
    public Color[]? CentreVertexLight { get; init; }

    public bool HasCellCentres => CentreHeights != null;

    /// <summary>Cells along a chunk edge.</summary>
    public int CellsPerEdge => HeightResolution - 1;

    public float HeightAt(int x, int y) => Heights[(y * HeightResolution) + x];

    public bool IsHole(int x, int y) => Holes[(y * HoleResolution) + x];

    public Color VertexColorAt(int x, int y) => VertexColors[(y * HeightResolution) + x];

    public Color VertexLightAt(int x, int y) => VertexLight[(y * HeightResolution) + x];

    /// <summary>Height of the centre vertex of cell (x, y). Requires <see cref="HasCellCentres"/>.</summary>
    public float CentreHeightAt(int x, int y) => CentreHeights![(y * CellsPerEdge) + x];

    public Color CentreVertexColorAt(int x, int y) => CentreVertexColors![(y * CellsPerEdge) + x];

    public Color CentreVertexLightAt(int x, int y) => CentreVertexLight![(y * CellsPerEdge) + x];
}
