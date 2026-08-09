namespace WorldMapStudio;

/// <summary>What a layer contributes to a chunk.</summary>
public enum LandscapeLayerKind
{
    /// <summary>Occupies one of the chunk's texture slots.</summary>
    Texture,

    /// <summary>Contributes only to height, so it is never subject to the texture budget.</summary>
    Height,
}

/// <summary>
/// A per-chunk slot, and — more importantly — a <em>responsibility domain</em>. A layer is what lets
/// the system say "grass_highlight_1 and grass_highlight_2 are the same job, you cannot have both
/// here": two entities binding different materials to one layer in one chunk is a conflict, and the
/// higher priority wins.
///
/// Layers are global to the project; entities reference them through instance parameters rather than
/// hardcoding them, because which layers exist is the user's decision, not source code.
/// </summary>
public sealed class LandscapeLayer : CatalogEntity, IKeyedCatalogEntity
{
    public string Name { get; set; } = "Layer";

    public LandscapeLayerKind Kind { get; set; } = LandscapeLayerKind.Texture;

    /// <summary>
    /// Whether this layer is the opaque bottom of a chunk. A base layer writes no alpha, so at most
    /// one may be bound per chunk, and it must sort below every other texture layer.
    /// </summary>
    public bool IsBase { get; set; }

    /// <summary>How important this layer is when the texture budget is exceeded. Higher survives.</summary>
    public int Priority { get; set; }

    /// <summary>
    /// Compositing order for texture layers, evaluation order for height layers — one number serving
    /// both, because a road that flattens must be able to run after a hill that raises. Unique across
    /// the project, so two layers are never ambiguously ordered.
    /// </summary>
    public int DrawOrder { get; set; }

    /// <inheritdoc />
    public int? RecordId { get; set; }

    /// <inheritdoc />
    public bool IsSaved { get; set; }

    public override string DisplayName => Name;

    /// <summary>Whether this layer consumes one of the chunk's texture slots.</summary>
    public bool UsesTextureSlot => Kind == LandscapeLayerKind.Texture;
}
