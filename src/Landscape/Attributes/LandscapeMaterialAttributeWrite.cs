namespace WorldMapStudio;

/// <summary>
/// One entry in a material's list of terrain-attribute writes — the open-ended counterpart of the
/// fixed <c>HeightFunction</c>/<c>HeightParameters</c> pair the other five output kinds carry. A
/// material declares as many of these as the map has attributes to write, so they are child rows keyed
/// to their owner by <see cref="MaterialId"/> rather than more columns on the material, following the
/// same flat-table shape as <see cref="TerrainAttributeValue"/>.
/// </summary>
public sealed class LandscapeMaterialAttributeWrite : CatalogEntity, IKeyedCatalogEntity, ILandscapeCatalogEntity
{
    /// <summary>The map this row belongs to — carried from the owning material.</summary>
    public MapId Map { get; set; } = new(0);

    /// <summary><see cref="IKeyedCatalogEntity.RecordId"/> of the owning <see cref="LandscapeMaterial"/>.</summary>
    public int MaterialId { get; set; }

    /// <summary>The attribute this write targets, as <c>"key"</c> or <c>"key:swizzle"</c> — parsed by
    /// <see cref="TerrainAttributeBinding"/>.</summary>
    public string Attribute { get; set; } = "";

    /// <summary>Id of the <see cref="ILandscapeAttributeFunction"/> this write runs.</summary>
    public string Function { get; set; } = "";

    /// <summary>Serialized parameter values for <see cref="Function"/>.</summary>
    public string Parameters { get; set; } = "";

    /// <inheritdoc />
    public int? RecordId { get; set; }

    /// <inheritdoc />
    public bool IsSaved { get; set; }

    public override string DisplayName => Attribute.Length > 0 ? Attribute : "attribute write";

    public TerrainAttributeBinding Binding => TerrainAttributeBinding.Parse(Attribute);
}
