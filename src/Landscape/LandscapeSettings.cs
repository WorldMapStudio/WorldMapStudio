using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>How a chunk's heightmap is stored, chosen to match the export target.</summary>
public enum HeightEncoding
{
    /// <summary>Raw 32-bit floats. Lossless, but twice the size and rarely what a game format wants.</summary>
    Float32,

    /// <summary>16-bit unsigned, mapped through <see cref="LandscapeSettings.HeightOffset"/> and
    /// <see cref="LandscapeSettings.HeightScale"/>. What most splatting formats actually store.</summary>
    UInt16,
}

/// <summary>
/// The landscape settings of one map: how terrain is represented, and the constraint the slot
/// resolver has to satisfy. Supplied by an <see cref="ILandscapeProfile"/> for a known export target,
/// or edited by hand.
///
/// These are not frozen — in a system where every output is derived, changing them is a map-wide
/// rebuild rather than data loss (see <see cref="ChangeCost"/>). The editor warns; it does not refuse.
/// </summary>
public sealed class LandscapeSettings
{
    /// <summary>Extent of one chunk in world units.</summary>
    public float ChunkWorldSize { get; set; } = 64.0f;

    /// <summary>Heightmap vertices along a chunk edge. Edge rows are shared with the neighbour.</summary>
    public int ChunkHeightResolution { get; set; } = 33;

    /// <summary>Alpha texels along a chunk edge.</summary>
    public int ChunkAlphaResolution { get; set; } = 64;

    /// <summary>
    /// Hole cells along a chunk edge. Deliberately independent of <see cref="ChunkHeightResolution"/>
    /// and <see cref="ChunkAlphaResolution"/> — a hole is a coarse "is this quad missing" grid, matching
    /// how export targets like WoW's ADT format encode holes at a fixed, low resolution.
    /// </summary>
    public int ChunkHoleResolution { get; set; } = 8;

    public HeightEncoding HeightEncoding { get; set; } = HeightEncoding.Float32;

    /// <summary>World height that encoded zero represents. Only used by <see cref="HeightEncoding.UInt16"/>.</summary>
    public float HeightOffset { get; set; }

    /// <summary>World units per encoded step. Only used by <see cref="HeightEncoding.UInt16"/>.</summary>
    public float HeightScale { get; set; } = 1.0f / 512.0f;

    /// <summary>Bits per texel in an output alpha layer.</summary>
    public int AlphaBitDepth { get; set; } = 8;

    /// <summary>Whether the layer alphas of a chunk are normalized to sum to 1, or composited independently.</summary>
    public bool AlphaNormalized { get; set; }

    /// <summary>The chunk coordinate sitting at the world origin.</summary>
    public int OriginChunkX { get; set; }

    public int OriginChunkY { get; set; }

    /// <summary>How far chunks may extend from the origin in either direction. Nothing is preallocated.</summary>
    public int ChunkLimit { get; set; } = 64;

    /// <summary>Textures one chunk may reference, <em>including</em> the base layer.</summary>
    public int TextureLimit { get; set; } = 4;

    /// <summary>
    /// Material for the base slot when no layer claims one, so a chunk whose claims were all dropped
    /// still renders something. Null until the user picks one; reported as a problem when a chunk
    /// needs it and it is missing.
    /// </summary>
    public int? FallbackMaterialId { get; set; }

    /// <summary>The profile these settings came from, for display. Empty when hand-authored.</summary>
    public string ProfileName { get; set; } = "";

    public LandscapeSettings Clone() => (LandscapeSettings)MemberwiseClone();

    /// <summary>Reasons these settings cannot be used, or empty when they are valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (ChunkWorldSize <= 0.0f)
        {
            problems.Add("Chunk world size must be greater than zero.");
        }

        // Shared edge rows mean a chunk edge needs at least two vertices to have an interior at all.
        if (ChunkHeightResolution < 2)
        {
            problems.Add("Chunk height resolution needs at least 2 vertices per edge.");
        }

        if (ChunkAlphaResolution < 1)
        {
            problems.Add("Chunk alpha resolution needs at least 1 texel per edge.");
        }

        if (ChunkHoleResolution < 1)
        {
            problems.Add("Chunk hole resolution needs at least 1 cell per edge.");
        }

        if (AlphaBitDepth is not (8 or 16))
        {
            problems.Add("Alpha bit depth must be 8 or 16.");
        }

        // One texture is the base layer, so a limit below 1 cannot render anything at all.
        if (TextureLimit < 1)
        {
            problems.Add("Texture limit must be at least 1 (the base layer).");
        }

        if (ChunkLimit < 1)
        {
            problems.Add("Chunk limit must be at least 1.");
        }

        if (HeightEncoding == HeightEncoding.UInt16 && HeightScale <= 0.0f)
        {
            problems.Add("Height scale must be greater than zero for 16-bit height.");
        }

        return problems;
    }

    /// <summary>
    /// What changing a setting from <paramref name="previous"/> costs. Nothing here is destructive to
    /// <em>inputs</em> — entities are untouched — but a rebuild throws away every built chunk.
    /// </summary>
    public static LandscapeChangeCost ChangeCost(LandscapeSettings previous, LandscapeSettings next)
    {
        bool rebuild =
            previous.ChunkWorldSize != next.ChunkWorldSize ||
            previous.ChunkHeightResolution != next.ChunkHeightResolution ||
            previous.ChunkAlphaResolution != next.ChunkAlphaResolution ||
            previous.ChunkHoleResolution != next.ChunkHoleResolution ||
            previous.HeightEncoding != next.HeightEncoding ||
            previous.HeightOffset != next.HeightOffset ||
            previous.HeightScale != next.HeightScale ||
            previous.AlphaBitDepth != next.AlphaBitDepth ||
            previous.AlphaNormalized != next.AlphaNormalized;

        // Lowering the limit re-runs slot resolution and can drop layers that used to fit.
        bool resolve = next.TextureLimit < previous.TextureLimit ||
                       previous.FallbackMaterialId != next.FallbackMaterialId;

        if (rebuild)
        {
            return LandscapeChangeCost.Rebuild;
        }

        return resolve ? LandscapeChangeCost.Reresolve : LandscapeChangeCost.Free;
    }
}

/// <summary>What a settings change forces the landscape system to redo.</summary>
public enum LandscapeChangeCost
{
    /// <summary>Addressing only: chunk coordinates are absolute, so nothing needs redoing.</summary>
    Free,

    /// <summary>Slot resolution changes, so layers may appear or disappear, but channels stand.</summary>
    Reresolve,

    /// <summary>Every built chunk is invalid and has to be rebuilt from its entities.</summary>
    Rebuild,
}
