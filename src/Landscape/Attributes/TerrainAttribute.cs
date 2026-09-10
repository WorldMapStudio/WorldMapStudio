using System;

namespace WorldMapStudio;

/// <summary>
/// What one component of a <see cref="TerrainAttribute"/> means. Used only by the editor's parameter
/// UI and problem reporting — every kind stores an unsigned integer of the declared width, so there is
/// one allocation path, one overlay upload and one export read. <see cref="Float"/> reinterprets those
/// bits.
/// </summary>
public enum TerrainAttributeKind
{
    /// <summary>A plain number.</summary>
    Int,

    /// <summary>0 or 1.</summary>
    Bool,

    /// <summary>A bitfield with named bits, from the attribute's <see cref="TerrainAttributeValue"/> rows.</summary>
    Flags,

    /// <summary>One of a set of named values, from the attribute's <see cref="TerrainAttributeValue"/> rows.</summary>
    Enum,

    /// <summary>A row id in the catalog named by <see cref="TerrainAttribute.CatalogName"/>.</summary>
    CatalogRef,

    /// <summary>An IEEE-754 float carried in the component's bits.</summary>
    Float,
}

/// <summary>
/// A declaration: a named, open-ended terrain output kind that materials write, the editor renders and
/// an exporter reads — height, alpha, holes, vertex color and vertex light are the five hardcoded
/// special cases of this shape. Map-scoped, and sits with <see cref="LandscapeChannel"/> /
/// <see cref="LandscapeLayer"/> / <see cref="LandscapeMaterial"/> in <see cref="LandscapeCatalog"/>.
///
/// It declares no storage, exactly as <see cref="LandscapeChannel"/> declares no pixels: attribute
/// values are derived per build, never persisted. What tells <c>Evaluate</c> which buffers to
/// allocate, and an exporter what to look up, is this row's shape — cells per chunk edge, components,
/// element width — plus the default every component starts at.
///
/// The one invariant separating an attribute from a channel is that nothing ever filters it: no halo,
/// no cross-chunk blend, no bilinear shader sample. A cell reaches the exporter as the exact integer a
/// function wrote.
/// </summary>
public sealed class TerrainAttribute : CatalogEntity, IKeyedCatalogEntity, ILandscapeCatalogEntity
{
    /// <summary>The map this attribute belongs to.</summary>
    public MapId Map { get; set; } = new(0);

    /// <summary>Stable key materials bind their writes to, and an exporter looks up, e.g. <c>terrain.area</c>.
    /// Renaming it orphans every material write bound to it.</summary>
    public string Key { get; set; } = "attribute";

    public string Name { get; set; } = "Attribute";

    public string Description { get; set; } = "";

    /// <summary>Cells along a chunk edge — 1 for a per-chunk scalar, 8/16/… for a raster. Independent
    /// of the height, alpha and hole resolutions, the same reasoning as
    /// <see cref="LandscapeSettings.ChunkHoleResolution"/>.</summary>
    public int CellsPerChunkEdge { get; set; } = 1;

    /// <summary>Values per cell: 1 for a scalar, 3 for RGB-shaped, 4 for RGBA-shaped — the same axis as
    /// <see cref="LandscapeChannel.Components"/>. Every component shares <see cref="Kind"/>.</summary>
    public int Components { get; set; } = 1;

    /// <summary>Bits per component: 8, 16 or 32 — the same axis and vocabulary as
    /// <see cref="LandscapeChannel.BitDepth"/>. Keeps a large, narrow attribute from costing a fixed
    /// 32 bits per cell.</summary>
    public int ElementWidth { get; set; } = 32;

    /// <summary>Optional per-component labels, comma-separated, defaulting to R/G/B/A — they name the
    /// components in the parameter editor and the overlay picker.</summary>
    public string ComponentNames { get; set; } = "";

    /// <summary>What one component means — describes every component of this attribute.</summary>
    public TerrainAttributeKind Kind { get; set; } = TerrainAttributeKind.Int;

    /// <summary>For <see cref="TerrainAttributeKind.CatalogRef"/>: the catalog a value is a row id in,
    /// e.g. <c>Area</c>. Empty otherwise.</summary>
    public string CatalogName { get; set; } = "";

    /// <summary>What every component starts at each build, before any claim writes — like vertex color
    /// starts white.</summary>
    public uint DefaultValue { get; set; }

    /// <summary>True when a profile created this row, so a reseed updates it instead of duplicating it.</summary>
    public bool Seeded { get; set; }

    /// <inheritdoc />
    public int? RecordId { get; set; }

    /// <inheritdoc />
    public bool IsSaved { get; set; }

    public override string DisplayName => Name;

    /// <summary>Bytes one chunk of this attribute occupies — cells² × components × width.</summary>
    public int BytesPerChunk => CellsPerChunkEdge * CellsPerChunkEdge * Components * (ElementWidth / 8);

    /// <summary>Label for component <paramref name="index"/> — the authored name, or R/G/B/A.</summary>
    public string ComponentLabel(int index)
    {
        string[] names = ComponentNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (index >= 0 && index < names.Length)
        {
            return names[index];
        }

        return index is >= 0 and < 4 ? "RGBA"[index].ToString() : $"component {index}";
    }

    /// <summary>Whether <paramref name="value"/> fits <see cref="ElementWidth"/> bits — an over-wide
    /// write is a reported problem, not a silent truncation.</summary>
    public bool Fits(uint value) => ElementWidth >= 32 || value <= (1u << ElementWidth) - 1u;
}
