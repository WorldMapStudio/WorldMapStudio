using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

public sealed class EntityTagRecord : IKeyedRecord
{
    public int Id { get; set; }

    public string Name { get; set; } = "Tag";

    public int Color { get; set; }
}

/// <summary>Maps <see cref="EntityTagDefinition"/> to and from the Editor storage's <c>wms_tags</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class EntityTagDefinitionFactory(EditorStorage storage)
    : EditorCatalogFactory<EntityTagDefinition, EntityTagRecord>(storage)
{
    public const int NameMaxLength = 64;

    protected override string TableName => "wms_tags";

    public override void Configure(ModelBuilder model)
    {
        base.Configure(model);
        model.Entity<EntityTagRecord>(entity =>
        {
            entity.Property(record => record.Name).HasMaxLength(NameMaxLength);
            entity.HasIndex(record => record.Name).IsUnique();
        });
    }

    protected override EntityTagDefinition ToEntity(EntityTagRecord record) => new()
    {
        RecordId = record.Id,
        Name = record.Name,
        Color = record.Color,
    };

    protected override void WriteRecord(EntityTagDefinition entity, EntityTagRecord record)
    {
        record.Name = entity.Name;
        record.Color = entity.Color;
    }
}
