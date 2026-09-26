using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace WorldMapStudio;

/// <summary>
/// Everything <see cref="EditorStorage.ReconcileChunkChangesAsync"/> needs, gathered by
/// <see cref="ChunkChangeLog.RecordCommit"/> on the main thread before the reconcile hops to the pool.
/// </summary>
/// <param name="TouchedByMap">Per map, the chunks the commit could have changed and the world region
/// they span — see <see cref="ChunkChangeLog"/>'s remarks on why both are decided together.</param>
/// <param name="Grids">Every touched map's grid, already resolved live on the main thread. A map
/// discovered only while stamping <paramref name="EditedSharedResources"/>' unloaded placements is not
/// in here yet; the reconcile resolves and adds it through <see cref="LandscapeSystem.LoadSettingsForAsync"/>.</param>
/// <param name="LoadedEntityIds">Record ids of entities the scene currently has loaded — an edited
/// shared resource's placement among these was already covered by a snapshot, so its unloaded-placement
/// stamp is skipped.</param>
/// <param name="EditedSharedResources">Distinct (resource type, id) pairs a committed edit touched.</param>
/// <param name="CatalogChangedMaps">Maps with a committed edit to an <see cref="ILandscapeCatalogEntity"/>,
/// whose every chunk is restamped since nothing narrows down which one moved.</param>
public sealed record ChunkChangeReconcilePlan(
    IReadOnlyDictionary<MapId, (IReadOnlyCollection<ChunkCoord> Chunks, Aabb Region)> TouchedByMap,
    IReadOnlyDictionary<MapId, LandscapeGrid?> Grids,
    IReadOnlyCollection<int> LoadedEntityIds,
    IReadOnlyList<(Type Type, int Id)> EditedSharedResources,
    IReadOnlyList<MapId> CatalogChangedMaps);

public sealed partial class EditorStorage
{
    private static readonly HashSet<long> NoLoadedKeys = [];

    /// <summary>
    /// Brings the chunk change log back in line with what a commit left behind, in one write lock and
    /// one transaction: occupancy reads, the upsert/delete they decide, unloaded shared-resource
    /// placement stamps, and catalog-wide touch-alls. See <see cref="ChunkChangeLog.RecordCommit"/>,
    /// which this replaces three-to-N separate stalls for.
    /// </summary>
    public async Task ReconcileChunkChangesAsync(ChunkChangeReconcilePlan plan)
    {
        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();
        await context.Database.OpenConnectionAsync().ConfigureAwait(false);
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync().ConfigureAwait(false);
        DbTransaction dbTransaction = transaction.GetDbTransaction();

        var grids = new Dictionary<MapId, LandscapeGrid?>(plan.Grids);

        async Task<LandscapeGrid?> GridForAsync(MapId map)
        {
            if (grids.TryGetValue(map, out LandscapeGrid? cached))
            {
                return cached;
            }

            LandscapeGrid? grid = await Context.Landscape.LoadSettingsForAsync(map).ConfigureAwait(false) is { } settings
                ? new LandscapeGrid(settings)
                : null;
            grids[map] = grid;
            return grid;
        }

        foreach ((MapId map, (IReadOnlyCollection<ChunkCoord> touched, Aabb region)) in plan.TouchedByMap)
        {
            var present = new List<(int Map, int X, int Y)>();
            var vacated = new List<(int Map, int X, int Y)>();

            if (await GridForAsync(map).ConfigureAwait(false) is { } grid)
            {
                var occupied = new HashSet<ChunkCoord>();
                foreach (Aabb bounds in await OccupiedChunkBoundsCoreAsync(map, region).ConfigureAwait(false))
                {
                    foreach (ChunkCoord coord in grid.Overlapping(bounds))
                    {
                        occupied.Add(coord);
                    }
                }

                foreach (ChunkCoord coord in touched)
                {
                    (occupied.Contains(coord) ? present : vacated).Add((map.Value, coord.X, coord.Y));
                }
            }
            else
            {
                // No landscape settings at all for this map any more: nothing can still occupy it.
                vacated.AddRange(touched.Select(coord => (map.Value, coord.X, coord.Y)));
            }

            await UpsertChunkChangesWithinTransactionAsync(context, dbTransaction, present).ConfigureAwait(false);
            await RemoveChunkChangesWithinTransactionAsync(context, dbTransaction, vacated).ConfigureAwait(false);
        }

        var resourcePresent = new List<(int Map, int X, int Y)>();
        foreach ((Type type, int id) in plan.EditedSharedResources)
        {
            IResourceReferencingPersistence? persistence = ComponentPersistence
                .OfType<IResourceReferencingPersistence>()
                .FirstOrDefault(candidate => candidate.ReferencedResourceType == type);
            if (persistence == null)
            {
                continue;
            }

            foreach ((int entityId, MapId map, Aabb bounds) in
                await persistence.ReferencingBoundsAsync(context, id).ConfigureAwait(false))
            {
                if (plan.LoadedEntityIds.Contains(entityId) || await GridForAsync(map).ConfigureAwait(false) is not { } grid)
                {
                    continue;
                }

                foreach (ChunkCoord coord in grid.Overlapping(bounds))
                {
                    resourcePresent.Add((map.Value, coord.X, coord.Y));
                }
            }
        }

        await UpsertChunkChangesWithinTransactionAsync(context, dbTransaction, resourcePresent).ConfigureAwait(false);

        foreach (MapId map in plan.CatalogChangedMaps)
        {
            await TouchAllChunkChangesWithinTransactionAsync(context, dbTransaction, map.Value).ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Chunk-ownership bounds of every stored scene entity overlapping <paramref name="region"/>: from
    /// each factory's own cheap projection where it has one (see
    /// <see cref="ISceneEntityFactory.OccupiedChunkBoundsAsync"/>), or from a full <see cref="ISceneEntityFactory.ScanAsync"/>
    /// and <see cref="SceneEntity.WorldChunkBounds"/> off the built result where it doesn't. Assumes the
    /// caller already holds <see cref="Lock"/> (a reader or, as the reconcile does, the writer) — unlike
    /// this file's other reads, it does not acquire it itself.
    /// </summary>
    private async Task<IReadOnlyList<Aabb>> OccupiedChunkBoundsCoreAsync(MapId map, Aabb region)
    {
        var bounds = new List<Aabb>();
        var fallback = new List<ISceneEntityFactory>();
        foreach (ISceneEntityFactory factory in SceneFactories)
        {
            IReadOnlyList<Aabb>? projected = await factory.OccupiedChunkBoundsAsync(map, region).ConfigureAwait(false);
            if (projected != null)
            {
                bounds.AddRange(projected);
            }
            else
            {
                fallback.Add(factory);
            }
        }

        foreach (ISceneEntityFactory factory in fallback)
        {
            SceneEntityScan scan = await factory.ScanAsync(map, region, NoLoadedKeys, publishing: false).ConfigureAwait(false);
            foreach (SceneEntity entity in scan.Built)
            {
                if (entity.WorldChunkBounds is { } entityBounds)
                {
                    bounds.Add(entityBounds);
                }
            }
        }

        return bounds;
    }
}
