namespace WorldMapStudio;

/// <summary>
/// One named value of a <see cref="TerrainAttribute"/> declared as <see cref="TerrainAttributeKind.Enum"/>
/// or <see cref="TerrainAttributeKind.Flags"/> — the enum's cases, or the bitfield's named bits. A flat
/// child row keyed to its owner by <see cref="AttributeId"/> so <see cref="MigrationSystem"/> picks the
/// table up with no hand-written SQL, the same shape every other landscape catalog row has.
/// </summary>
public sealed class TerrainAttributeValue : CatalogEntity, IKeyedCatalogEntity, ILandscapeCatalogEntity
{
    /// <summary>The map this row belongs to — carried from the owning attribute so a committed edit
    /// stamps the right map's chunks.</summary>
    public MapId Map { get; set; } = new(0);

    /// <summary><see cref="IKeyedCatalogEntity.RecordId"/> of the owning <see cref="TerrainAttribute"/>.</summary>
    public int AttributeId { get; set; }

    /// <summary>The numeric value, or the bit for a <see cref="TerrainAttributeKind.Flags"/> attribute.</summary>
    public long Value { get; set; }

    public string Name { get; set; } = "";

    /// <inheritdoc />
    public int? RecordId { get; set; }

    /// <inheritdoc />
    public bool IsSaved { get; set; }

    public override string DisplayName => $"{Value} = {Name}";
}
