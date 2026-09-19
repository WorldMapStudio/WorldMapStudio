using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>
/// One entity that carries editor-side data: the identity every tag and component row is keyed on.
/// A native <see cref="MapSceneEntity"/> always has one, with a <see cref="MapEntityRecord"/> sharing its
/// id. An entity stored in another table has one only once it carries tags or attached components, and
/// names that table's row through <see cref="Source"/> and <see cref="SourceKey"/>.
/// </summary>
public sealed class EntityRecord
{
    public int Id { get; set; }

    /// <summary>The bridging factory's <see cref="ISceneEntityFactory.BridgeSource"/>; null for a native entity.</summary>
    public string? Source { get; set; }

    public long? SourceKey { get; set; }
}

public sealed partial class EditorDbContext
{
    public DbSet<EntityRecord> Entities => Set<EntityRecord>();
}

/// <summary>Declares the <c>wms_entities</c> table and the <c>wms_entity_tags</c> rows hanging off it.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class EntityTableConfiguration : ITableConfiguration
{
    public const int SourceMaxLength = 64;

    // The subsystem generator constructs every EditorStorage subsystem with its storage.
    public EntityTableConfiguration(EditorStorage storage)
    {
    }

    public void Configure(ModelBuilder model)
    {
        model.Entity<EntityRecord>(entity =>
        {
            entity.ToTable("wms_entities");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Id).ValueGeneratedNever();
            entity.Property(record => record.Source).HasMaxLength(SourceMaxLength);

            // Null pairs (native rows) never collide in a unique index.
            entity.HasIndex(record => new { record.Source, record.SourceKey }).IsUnique();
        });

        model.Entity<EntityTagRecord>(entity =>
        {
            entity.ToTable("wms_entity_tags");
            entity.HasKey(record => new { record.EntityId, record.TagId });
            entity.HasOne<EntityRecord>()
                .WithMany()
                .HasForeignKey(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<TagDefinitionRecord>()
                .WithMany()
                .HasForeignKey(record => record.TagId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(record => record.TagId);
        });
    }
}
