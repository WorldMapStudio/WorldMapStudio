namespace WorldMapStudio;

/// <summary>
/// A named, project-wide label a scene entity can carry any number of. Catalog-backed, so one tag
/// spans every map and every kind of entity, and an entity can be tagged with a tag created earlier in
/// the same uncommitted session.
/// </summary>
public sealed class EntityTagDefinition : CatalogEntity, IKeyedCatalogEntity
{
    [ScriptProperty(Mutable = true)]
    public string Name { get; set; } = "Tag";

    /// <summary>Packed <c>0xRRGGBB</c>.</summary>
    [ScriptProperty(Mutable = true)]
    public int Color { get; set; } = 0x808080;

    /// <inheritdoc />
    public int? RecordId { get; set; }

    /// <inheritdoc />
    public bool IsSaved { get; set; }

    public override string DisplayName => Name;
}
