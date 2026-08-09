namespace WorldMapStudio;

/// <summary>
/// What actually gets bound into a chunk's texture slot: an image, the alpha function that decides
/// where it shows, and optionally a height function that deforms the ground under it.
///
/// Functions are referenced by id rather than by type, because they are discovered at startup (and
/// may later live in a hot-swappable assembly). The parameter bags are opaque here — Phase 3 gives
/// them structure once functions declare what they take. Until then they round-trip untouched, so
/// authoring a material now does not lose data later.
/// </summary>
public sealed class LandscapeTextureMaterial : CatalogEntity
{
    public string Name { get; set; } = "Material";

    /// <summary>Resource path of the texture image this material paints.</summary>
    public string TexturePath { get; set; } = "";

    /// <summary>Id of the alpha function deciding this material's coverage. Required to render.</summary>
    public string AlphaFunction { get; set; } = "";

    /// <summary>Serialized parameter values for <see cref="AlphaFunction"/>; opaque until Phase 3.</summary>
    public string AlphaParameters { get; set; } = "";

    /// <summary>Id of an optional height function, or empty when this material does not deform.</summary>
    public string HeightFunction { get; set; } = "";

    /// <summary>Serialized parameter values for <see cref="HeightFunction"/>; opaque until Phase 3.</summary>
    public string HeightParameters { get; set; } = "";

    /// <summary>Primary key of the backing row once persisted; null until first saved.</summary>
    public int? RecordId { get; set; }

    public override string DisplayName => Name;

    public bool DeformsHeight => HeightFunction.Length > 0;
}
