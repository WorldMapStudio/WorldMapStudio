using System;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>When a chunk was last touched by a committed edit. See <see cref="ChunkChangeLog"/>.</summary>
public sealed class ChunkChangeRecord
{
    public int MapId { get; set; }

    public int ChunkX { get; set; }

    public int ChunkY { get; set; }

    public DateTime LastEditedUtc { get; set; }
}

/// <summary>
/// A stable id an export profile has assigned to an entity — for a target format that needs one (e.g.
/// an ADT placement's unique id) but has no id of its own to reuse. Scoped per profile, keyed on the
/// entity's runtime <see cref="EntityId"/> rather than any catalog/record id, since the same entity
/// keeps this assignment across saves that change nothing else about it. See
/// <see cref="ExportSystem.LoadExportedEntityIdsAsync"/>/<see cref="ExportSystem.UpsertExportedEntityIdsAsync"/>.
/// </summary>
public sealed class ExportedEntityIdRecord
{
    public string ProfileId { get; set; } = "";

    public long EntityId { get; set; }

    public long AllocatedId { get; set; }
}

public sealed partial class EditorDbContext
{
    public DbSet<ChunkChangeRecord> ChunkChanges => Set<ChunkChangeRecord>();

    public DbSet<ExportedEntityIdRecord> ExportedEntityIds => Set<ExportedEntityIdRecord>();
}

/// <summary>
/// Declares the tables above. Neither backs an <see cref="IEntity"/> or has any other self-registered
/// owner — they're written directly by <see cref="EditorStorage"/>'s own Upsert/Load methods — so this
/// is a standalone <see cref="ITableConfiguration"/> rather than a side effect of some other seam.
/// </summary>
[Subsystem(nameof(EditorStorage))]
public sealed class ChunkChangeTableConfiguration : ITableConfiguration
{
    public float Priority => 0.0f;

    public ChunkChangeTableConfiguration(EditorStorage storage)
    {
    }

    public void Configure(ModelBuilder model)
    {
        model.Entity<ChunkChangeRecord>(entity =>
        {
            entity.ToTable("wms_chunk_changes");
            entity.HasKey(record => new { record.MapId, record.ChunkX, record.ChunkY });
        });

        model.Entity<ExportedEntityIdRecord>(entity =>
        {
            entity.ToTable("wms_exported_entity_ids");
            entity.HasKey(record => new { record.ProfileId, record.EntityId });
            entity.Property(record => record.ProfileId).HasMaxLength(128);
        });
    }
}
