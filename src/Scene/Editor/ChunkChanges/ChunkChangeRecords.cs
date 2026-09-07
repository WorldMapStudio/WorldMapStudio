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

public sealed partial class EditorDbContext
{
    public DbSet<ChunkChangeRecord> ChunkChanges => Set<ChunkChangeRecord>();
}

/// <summary>
/// Declares the table above. It backs no <see cref="IEntity"/> and has no other self-registered owner
/// — it's written directly by <see cref="EditorStorage"/>'s own Upsert/Load methods — so this is a
/// standalone <see cref="ITableConfiguration"/> rather than a side effect of some other seam.
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
    }
}
