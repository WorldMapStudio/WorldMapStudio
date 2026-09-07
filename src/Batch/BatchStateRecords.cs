using System;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>One key/value pair an operation keeps between runs. See <see cref="BatchState"/>.</summary>
public sealed class BatchStateRecord
{
    public string OperationId { get; set; } = "";

    public string Key { get; set; } = "";

    public string Value { get; set; } = "";

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed partial class EditorDbContext
{
    public DbSet<BatchStateRecord> BatchState => Set<BatchStateRecord>();
}

/// <summary>
/// Declares <c>wms_batch_state</c>. Backs no <see cref="IEntity"/> and has no self-registered owner —
/// it's written directly by <see cref="EditorStorage"/>'s own methods — so it's a standalone
/// <see cref="ITableConfiguration"/>, like <see cref="ChunkChangeTableConfiguration"/>.
/// </summary>
[Subsystem(nameof(EditorStorage))]
public sealed class BatchStateTableConfiguration : ITableConfiguration
{
    public float Priority => 0.0f;

    public BatchStateTableConfiguration(EditorStorage storage)
    {
    }

    public void Configure(ModelBuilder model)
    {
        model.Entity<BatchStateRecord>(entity =>
        {
            entity.ToTable("wms_batch_state");
            entity.HasKey(record => new { record.OperationId, record.Key });
            entity.Property(record => record.OperationId).HasMaxLength(128);

            // Both halves of the key live under utf8mb4's 767-byte index limit; 128 + 191 fits.
            entity.Property(record => record.Key).HasMaxLength(191);
        });
    }
}
