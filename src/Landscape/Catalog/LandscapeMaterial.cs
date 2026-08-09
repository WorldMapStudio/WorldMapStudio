namespace WorldMapStudio;

/// <summary>
/// What a layer <em>does</em> when something claims it. Two independent halves, either of which may
/// be absent:
///
/// <list type="bullet">
/// <item>a texture and the alpha function deciding where it shows, used when bound to a texture layer;</item>
/// <item>a height function, used when bound to a height layer.</item>
/// </list>
///
/// A material may be texture-only, height-only, or both — a road that paints gravel <em>and</em>
/// flattens the ground under it is one material, not two. This mirrors the layer model: a height
/// layer is a texture layer that defines no texture, so a height material is a material that defines
/// no texture.
///
/// Which half is required therefore depends on what a material is bound to, and that binding is made
/// per entity and per chunk. So it cannot be checked here — the builder reports a material that is
/// missing the half its layer needs.
///
/// Functions are referenced by id rather than by type, because they are discovered at startup and may
/// later live in a hot-swappable assembly.
/// </summary>
public sealed class LandscapeMaterial : CatalogEntity, IKeyedCatalogEntity
{
    public string Name { get; set; } = "Material";

    /// <summary>Resource path of the texture image this material paints. Empty for a height-only material.</summary>
    public string TexturePath { get; set; } = "";

    /// <summary>Id of the alpha function deciding coverage. Required only on a texture layer.</summary>
    public string AlphaFunction { get; set; } = "";

    /// <summary>Serialized parameter values for <see cref="AlphaFunction"/>.</summary>
    public string AlphaParameters { get; set; } = "";

    /// <summary>Id of the height function. Required only on a height layer.</summary>
    public string HeightFunction { get; set; } = "";

    /// <summary>Serialized parameter values for <see cref="HeightFunction"/>.</summary>
    public string HeightParameters { get; set; } = "";

    /// <inheritdoc />
    public int? RecordId { get; set; }

    /// <inheritdoc />
    public bool IsSaved { get; set; }

    public override string DisplayName => Name;

    /// <summary>Whether this material can fill a texture slot.</summary>
    public bool PaintsTexture => AlphaFunction.Length > 0;

    /// <summary>Whether this material can deform a height layer.</summary>
    public bool DeformsHeight => HeightFunction.Length > 0;
}
