using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>
/// Deletes a map's landscape catalog — channels, attributes, attribute values, material attribute
/// writes, layers and materials — as one <see cref="IMapScopedData"/> owner, rather than one per table:
/// the six are one conceptual unit (a map's landscape can't be rebuilt with only some of them gone) and
/// two of them reference the other two by id, so the delete order matters more than any one table's
/// count does on its own.
/// </summary>
[Subsystem(nameof(EditorStorage))]
public sealed class LandscapeCatalogMapData : IMapScopedData
{
    private readonly EditorStorage _storage;

    public LandscapeCatalogMapData(EditorStorage storage)
    {
        _storage = storage;
    }

    // Keyed directly on MapId, not through an entity — runs after MapSceneEntityFactory (priority 0),
    // though nothing here actually depends on the entities being gone yet.
    public float Priority => 1.0f;

    public string Label => "Landscape catalog";

    public async Task<int> CountAsync(EditorDbContext context, MapId map)
    {
        int total = 0;
        total += await _storage.CountWhereMapAsync<LandscapeChannelRecord>(context, nameof(LandscapeChannelRecord.MapId), map).ConfigureAwait(false);
        total += await _storage.CountWhereMapAsync<TerrainAttributeRecord>(context, nameof(TerrainAttributeRecord.MapId), map).ConfigureAwait(false);
        total += await _storage.CountWhereMapAsync<TerrainAttributeValueRecord>(context, nameof(TerrainAttributeValueRecord.MapId), map).ConfigureAwait(false);
        total += await _storage.CountWhereMapAsync<LandscapeMaterialAttributeWriteRecord>(context, nameof(LandscapeMaterialAttributeWriteRecord.MapId), map).ConfigureAwait(false);
        total += await _storage.CountWhereMapAsync<LandscapeLayerRecord>(context, nameof(LandscapeLayerRecord.MapId), map).ConfigureAwait(false);
        total += await _storage.CountWhereMapAsync<LandscapeMaterialRecord>(context, nameof(LandscapeMaterialRecord.MapId), map).ConfigureAwait(false);
        return total;
    }

    public async Task<IReadOnlySet<int>> MapIdsAsync(EditorDbContext context)
    {
        var ids = new HashSet<int>();
        ids.UnionWith(await context.Set<LandscapeChannelRecord>().AsNoTracking().Select(r => r.MapId).Distinct().ToListAsync().ConfigureAwait(false));
        ids.UnionWith(await context.Set<TerrainAttributeRecord>().AsNoTracking().Select(r => r.MapId).Distinct().ToListAsync().ConfigureAwait(false));
        ids.UnionWith(await context.Set<TerrainAttributeValueRecord>().AsNoTracking().Select(r => r.MapId).Distinct().ToListAsync().ConfigureAwait(false));
        ids.UnionWith(await context.Set<LandscapeMaterialAttributeWriteRecord>().AsNoTracking().Select(r => r.MapId).Distinct().ToListAsync().ConfigureAwait(false));
        ids.UnionWith(await context.Set<LandscapeLayerRecord>().AsNoTracking().Select(r => r.MapId).Distinct().ToListAsync().ConfigureAwait(false));
        ids.UnionWith(await context.Set<LandscapeMaterialRecord>().AsNoTracking().Select(r => r.MapId).Distinct().ToListAsync().ConfigureAwait(false));
        return ids;
    }

    /// <summary>Values and writes go first — they reference an attribute/material by id, not the other
    /// way around.</summary>
    public async Task DeleteAsync(EditorDbContext context, DbTransaction transaction, MapId map)
    {
        await _storage.DeleteWhereMapAsync<TerrainAttributeValueRecord>(context, transaction, nameof(TerrainAttributeValueRecord.MapId), map).ConfigureAwait(false);
        await _storage.DeleteWhereMapAsync<LandscapeMaterialAttributeWriteRecord>(context, transaction, nameof(LandscapeMaterialAttributeWriteRecord.MapId), map).ConfigureAwait(false);
        await _storage.DeleteWhereMapAsync<TerrainAttributeRecord>(context, transaction, nameof(TerrainAttributeRecord.MapId), map).ConfigureAwait(false);
        await _storage.DeleteWhereMapAsync<LandscapeMaterialRecord>(context, transaction, nameof(LandscapeMaterialRecord.MapId), map).ConfigureAwait(false);
        await _storage.DeleteWhereMapAsync<LandscapeLayerRecord>(context, transaction, nameof(LandscapeLayerRecord.MapId), map).ConfigureAwait(false);
        await _storage.DeleteWhereMapAsync<LandscapeChannelRecord>(context, transaction, nameof(LandscapeChannelRecord.MapId), map).ConfigureAwait(false);
    }
}
