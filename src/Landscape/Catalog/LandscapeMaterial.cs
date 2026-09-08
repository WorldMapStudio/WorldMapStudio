namespace WorldMapStudio;

/// <summary>
/// What a layer <em>does</em> when something claims it. Two independent halves, either of which may
/// be absent:
///
/// <list type="bullet">
/// <item>a texture and the alpha function deciding where it shows, used when bound to a texture layer;</item>
/// <item>a height function, used when bound to a height layer;</item>
/// <item>a hole function, marking cells of a bound layer as cut out of the mesh entirely;</item>
/// <item>a vertex color function, transforming the chunk's vertex color (a multiplier on albedo);</item>
/// <item>a vertex light function, transforming the chunk's vertex light (additive on albedo).</item>
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
public sealed class LandscapeMaterial : CatalogEntity, IKeyedCatalogEntity, ILandscapeCatalogEntity
{
    /// <summary>The map this material belongs to.</summary>
    public MapId Map { get; set; } = new(0);

    public string Name { get; set; } = "Material";

    /// <summary>Resource path of the texture image this material paints. Empty for a height-only material.</summary>
    public string TexturePath { get; set; } = "";

    /// <summary>
    /// Resource path of a grayscale texture driving this material's weight under
    /// <see cref="LandscapeTextureBlendMode.HeightBased"/> splatting — higher pixel values dominate over
    /// competing slots at that point, independent of <see cref="AlphaFunction"/> coverage. Empty means
    /// "no height preference" (a neutral mid-value), so a material authored before this existed still
    /// blends reasonably under height-based mode. Unused by <see cref="LandscapeTextureBlendMode.SequentialOver"/>/
    /// <see cref="LandscapeTextureBlendMode.WeightedSum"/>.
    /// </summary>
    public string BlendHeightTexturePath { get; set; } = "";

    /// <summary>Id of the alpha function deciding coverage. Required only on a texture layer.</summary>
    public string AlphaFunction { get; set; } = "";

    /// <summary>Serialized parameter values for <see cref="AlphaFunction"/>.</summary>
    public string AlphaParameters { get; set; } = "";

    /// <summary>Id of the height function. Required only on a height layer.</summary>
    public string HeightFunction { get; set; } = "";

    /// <summary>Serialized parameter values for <see cref="HeightFunction"/>.</summary>
    public string HeightParameters { get; set; } = "";

    /// <summary>Id of the hole function. Required only on a layer whose claim wants to cut holes.</summary>
    public string HoleFunction { get; set; } = "";

    /// <summary>Serialized parameter values for <see cref="HoleFunction"/>.</summary>
    public string HoleParameters { get; set; } = "";

    /// <summary>Id of the vertex color function. Transforms the chunk's accumulated vertex color,
    /// consumed by the shader as a multiplier over the splatted albedo.</summary>
    public string VertexColorFunction { get; set; } = "";

    /// <summary>Serialized parameter values for <see cref="VertexColorFunction"/>.</summary>
    public string VertexColorParameters { get; set; } = "";

    /// <summary>Id of the vertex light function. Transforms the chunk's accumulated vertex light,
    /// consumed by the shader as an additive term over the splatted albedo.</summary>
    public string VertexLightFunction { get; set; } = "";

    /// <summary>Serialized parameter values for <see cref="VertexLightFunction"/>.</summary>
    public string VertexLightParameters { get; set; } = "";

    /// <inheritdoc />
    public int? RecordId { get; set; }

    /// <inheritdoc />
    public bool IsSaved { get; set; }

    public override string DisplayName => Name;

    /// <summary>
    /// Whether this material fills a texture slot — it has an image to put there, or a function
    /// deciding where to put it. Either counts: a base layer needs only the image, since it is opaque
    /// and writes no alpha.
    /// </summary>
    public bool PaintsTexture => TexturePath.Length > 0 || AlphaFunction.Length > 0;

    /// <summary>Whether this material can deform a height layer.</summary>
    public bool DeformsHeight => HeightFunction.Length > 0;

    /// <summary>Whether this material can cut holes where it is bound.</summary>
    public bool CutsHole => HoleFunction.Length > 0;

    /// <summary>Whether this material transforms vertex color where it is bound.</summary>
    public bool PaintsVertexColor => VertexColorFunction.Length > 0;

    /// <summary>Whether this material transforms vertex light where it is bound.</summary>
    public bool PaintsVertexLight => VertexLightFunction.Length > 0;
}
