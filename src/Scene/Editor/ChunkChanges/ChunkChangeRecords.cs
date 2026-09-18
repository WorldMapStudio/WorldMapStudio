using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
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
public sealed class ChunkChangeTableConfiguration : ITableConfiguration, IMapScopedData
{
    private readonly EditorStorage _storage;

    // Keyed directly on MapId, not through an entity — see IMapScopedData's priority convention.
    public float Priority => 1.0f;

    public ChunkChangeTableConfiguration(EditorStorage storage)
    {
        _storage = storage;
    }

    public void Configure(ModelBuilder model)
    {
        model.Entity<ChunkChangeRecord>(entity =>
        {
            entity.ToTable("wms_chunk_changes");
            entity.HasKey(record => new { record.MapId, record.ChunkX, record.ChunkY });
        });
    }

    string IMapScopedData.Label => "Chunk changes";

    Task<int> IMapScopedData.CountAsync(EditorDbContext context, MapId map) =>
        _storage.CountWhereMapAsync<ChunkChangeRecord>(context, nameof(ChunkChangeRecord.MapId), map);

    async Task<IReadOnlySet<int>> IMapScopedData.MapIdsAsync(EditorDbContext context) =>
        (await context.ChunkChanges.AsNoTracking().Select(record => record.MapId).Distinct().ToListAsync().ConfigureAwait(false))
            .ToHashSet();

    Task IMapScopedData.DeleteAsync(EditorDbContext context, DbTransaction transaction, MapId map) =>
        _storage.DeleteWhereMapAsync<ChunkChangeRecord>(context, transaction, nameof(ChunkChangeRecord.MapId), map);
}
