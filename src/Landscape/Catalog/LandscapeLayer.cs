namespace WorldMapStudio;

/// <summary>
/// A <em>responsibility domain</em>, and the ordering of one. A layer is what lets the system say
/// "grass_highlight_1 and grass_highlight_2 are the same job, you cannot have both here": two
/// entities binding different materials to one layer in one chunk is a conflict, and the higher
/// priority wins.
///
/// A layer deliberately does <b>not</b> say whether it carries texture or height. That is decided by
/// the material bound to it, per entity and per chunk — a layer bound to a material with both halves
/// paints and deforms at once. Declaring it here as well would be the same fact in two places, free
/// to disagree.
///
/// Layers are scoped to the map they were authored for; entities reference them through instance
/// parameters rather than hardcoding them, because which layers exist is the user's decision, not
/// source code.
/// </summary>
public sealed class LandscapeLayer : CatalogEntity, IKeyedCatalogEntity, ILandscapeCatalogEntity
{
    /// <summary>The map this layer belongs to.</summary>
    public MapId Map { get; set; } = new(0);

    public string Name { get; set; } = "Layer";

    /// <summary>
    /// Whether this layer is the opaque bottom of a chunk. A base layer writes no alpha, so at most
    /// one may be bound per chunk, and everything that composites must sort above it.
    /// </summary>
    public bool IsBase { get; set; }

    /// <summary>How important this layer is when the texture budget is exceeded. Higher survives.</summary>
    public int Priority { get; set; }

    /// <summary>
    /// Compositing order for whatever texture this layer carries, evaluation order for whatever
    /// height it carries — one number serving both, because a road that flattens must be able to run
    /// after a hill that raises. Unique across the project, so two layers are never ambiguously
    /// ordered.
    /// </summary>
    public int DrawOrder { get; set; }

    /// <inheritdoc />
    public int? RecordId { get; set; }

    /// <inheritdoc />
    public bool IsSaved { get; set; }

    public override string DisplayName => Name;
}
