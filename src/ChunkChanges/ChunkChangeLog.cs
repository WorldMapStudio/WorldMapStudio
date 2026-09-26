using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>A chunk and when an edit last touched it.</summary>
public readonly record struct ChunkChange(MapId Map, ChunkCoord Coord, DateTime LastEditedUtc);

/// <summary>
/// Which chunks a map actually has, and when each was last edited. A plain member of
/// <see cref="EditorContext"/>, reconciled by <see cref="DatabaseSystem.Persist"/> on every commit.
///
/// <b>A row's existence is the claim that something is there.</b> Empty a chunk and its row goes,
/// which is what lets a consumer notice terrain that has been removed rather than only terrain that
/// has changed — see <see cref="RecordCommit"/>.
///
/// It never learns who read it or what anyone considers up to date. A consumer keeps one watermark
/// per whatever unit it cares about — a map, an output folder, the whole project — asks
/// <see cref="ChangedSinceAsync"/> what happened after it, and asks <see cref="ExistingAsync"/> what
/// is still there at all. That is its entire cache.
/// </summary>
public sealed class ChunkChangeLog
{
    private readonly EditorContext _context;

    public ChunkChangeLog(EditorContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Brings the log back in line with what the commit left behind. Every chunk the commit could have
    /// changed is re-decided from scratch: one that something still occupies is stamped with now, and
    /// one that nothing occupies any more loses its row.
    ///
    /// Deciding both from the same reconciled set is what makes a shrink or a delete legible
    /// downstream. Stamping alone would say "this changed" about a chunk that no longer exists, and
    /// leave a consumer no way to tell that apart from a chunk that changed and is still there.
    ///
    /// Occupancy comes from <see cref="SceneEntity.WorldChunkBounds"/>, so a map-spanning component
    /// bumps every chunk that already exists inside its reach without ever bringing one into being.
    ///
    /// Everything read here is main-thread state (live landscape settings, the loaded scene), captured
    /// into a <see cref="ChunkChangeReconcilePlan"/> before the reconcile itself runs as one
    /// <see cref="BlockingWork"/> stall instead of the three-to-N separate ones this used to be.
    /// </summary>
    public void RecordCommit(EditSession session, Func<IEntity, bool> wasCommitted)
    {
        EditorStorage storage = _context.Database.EditorStorage;
        var grids = new Dictionary<MapId, LandscapeGrid?>();

        LandscapeGrid? GridFor(MapId map)
        {
            if (!grids.TryGetValue(map, out LandscapeGrid? grid))
            {
                grid = _context.Landscape.LoadSettingsFor(map) is { } settings ? new LandscapeGrid(settings) : null;
                grids[map] = grid;
            }

            return grid;
        }

        Dictionary<MapId, (HashSet<ChunkCoord> Chunks, Aabb Region)> touchedByMap = TouchedByMap(session, wasCommitted, GridFor);
        HashSet<int> loadedEntityIds = _context.Scene.Entities.Select(entity => entity.RecordId).OfType<int>().ToHashSet();

        var plan = new ChunkChangeReconcilePlan(
            TouchedByMap: touchedByMap.ToDictionary(
                entry => entry.Key,
                entry => ((IReadOnlyCollection<ChunkCoord>)entry.Value.Chunks, entry.Value.Region)),
            Grids: grids,
            LoadedEntityIds: loadedEntityIds,
            EditedSharedResources: EditedSharedResources(session.History.UndoStack, wasCommitted),
            CatalogChangedMaps: CatalogChangedMaps(session.History.UndoStack, wasCommitted));

        BlockingWork.Run(() => storage.ReconcileChunkChangesAsync(plan));
    }

    /// <summary>
    /// Distinct (resource type, id) pairs a just-committed <see cref="ISharedResourceChunkCommand"/>
    /// touched. Their stored, not-currently-loaded placements are stamped from the resource's last-saved
    /// footprint (see <see cref="EditorStorage.ReconcileChunkChangesAsync"/>); a loaded placement is left
    /// to the snapshot pass in <see cref="TouchedByMap"/>, which measures it against the live edited
    /// resource instead.
    /// </summary>
    private static List<(Type Type, int Id)> EditedSharedResources(
        IEnumerable<IEditCommand> commands, Func<IEntity, bool> wasCommitted)
    {
        var resolved = new HashSet<(Type, int)>();
        var edited = new List<(Type, int)>();
        foreach (IEditCommand command in commands)
        {
            if (command is ISharedResourceChunkCommand shared
                && shared.SharedResource is (Type type, int id)
                && command.Targets.Any(wasCommitted)
                && resolved.Add((type, id)))
            {
                edited.Add((type, id));
            }
        }

        return edited;
    }

    /// <summary>
    /// Stamps every chunk the map still has as edited now. For a global change that reaches no entity
    /// and so never arrives at <see cref="RecordCommit"/> — a landscape settings save above all.
    /// Creates no rows: a chunk nothing occupies has nothing to re-export.
    /// </summary>
    public void MarkMapChanged(MapId map)
    {
        EditorStorage storage = _context.Database.EditorStorage;
        BlockingWork.Run(() => storage.TouchAllChunkChangesAsync(map.Value));
    }

    /// <summary>
    /// Records that a bulk producer has put terrain on <paramref name="chunks"/> of
    /// <paramref name="map"/>, stamping each with now. The counterpart to <see cref="RecordCommit"/>
    /// for output produced outside the edit-session path, which raises none of the snapshots
    /// <see cref="RecordCommit"/> reconciles from. A row's existence is still the claim that something
    /// is there — see the class remarks — so a consumer's reconcile pass keeps this terrain rather
    /// than seeing it as absent and dropping it.
    ///
    /// Only adds or restamps rows. Removing a bulk producer's output is <see cref="MarkChunksVacatedAsync"/>.
    /// </summary>
    public Task MarkChunksBuiltAsync(MapId map, IReadOnlyCollection<ChunkCoord> chunks)
    {
        if (chunks.Count == 0)
        {
            return Task.CompletedTask;
        }

        EditorStorage storage = _context.Database.EditorStorage;
        return storage.UpsertChunkChangesAsync(chunks.Select(coord => (map.Value, coord.X, coord.Y)).ToList());
    }

    /// <summary>Records that a bulk producer's terrain on <paramref name="chunks"/> is gone — drops
    /// each row, the same way emptying a chunk does through <see cref="RecordCommit"/>. For undoing a
    /// bulk run and for the chunks a re-run no longer produces.</summary>
    public Task MarkChunksVacatedAsync(MapId map, IReadOnlyCollection<ChunkCoord> chunks)
    {
        if (chunks.Count == 0)
        {
            return Task.CompletedTask;
        }

        EditorStorage storage = _context.Database.EditorStorage;
        return storage.RemoveChunkChangesAsync(chunks.Select(coord => (map.Value, coord.X, coord.Y)).ToList());
    }

    /// <summary>Every chunk edited strictly after <paramref name="since"/>, optionally on one map.
    /// The one query the whole caching model is built on.</summary>
    public async Task<IReadOnlyList<ChunkChange>> ChangedSinceAsync(DateTime since, MapId? map = null)
    {
        EditorStorage storage = _context.Database.EditorStorage;
        return await storage.LoadChangedSinceAsync(since, map?.Value).ConfigureAwait(false);
    }

    /// <summary>Every chunk a map still has, with when each was last edited. What a consumer
    /// reconciles its own output against to notice what has been removed.</summary>
    public Task<IReadOnlyList<ChunkChange>> ExistingAsync(MapId? map = null) =>
        ChangedSinceAsync(DateTime.MinValue, map);

    /// <summary>Every chunk in a coordinate rectangle that the map still has.</summary>
    public async Task<IReadOnlyList<ChunkChange>> InRangeAsync(ChunkRange range)
    {
        EditorStorage storage = _context.Database.EditorStorage;
        return await storage.LoadChunksInRangeAsync(range.Map, range.Min, range.Max).ConfigureAwait(false);
    }

    /// <summary>The newest edit time on record, so a consumer whose query returned nothing can still
    /// move its watermark forward. Null when nothing has ever been edited.</summary>
    public async Task<DateTime?> LatestEditUtcAsync(MapId? map = null)
    {
        EditorStorage storage = _context.Database.EditorStorage;
        return await storage.LoadLatestEditUtcAsync(map?.Value).ConfigureAwait(false);
    }

    /// <summary>
    /// Maps with a committed edit to an <see cref="ILandscapeCatalogEntity"/> this session. A catalog
    /// edit — a material's height amount, a layer's draw order — can move any chunk that binds it, and
    /// nothing cheap narrows that down (the same reason <see cref="LandscapeRebuilder"/> rebuilds the
    /// whole map on one), so the map's whole chunk set is restamped.
    /// </summary>
    internal static IReadOnlyList<MapId> CatalogChangedMaps(
        IEnumerable<IEditCommand> commands,
        Func<IEntity, bool> wasCommitted)
    {
        var maps = new HashSet<MapId>();
        foreach (IEditCommand command in commands)
        {
            foreach (IEntity target in command.Targets)
            {
                if (target is ILandscapeCatalogEntity catalog && wasCommitted(target))
                {
                    maps.Add(catalog.Map);
                }
            }
        }

        return maps.ToList();
    }

    /// <summary>
    /// Per map, the chunks this commit could have changed and the world region they span. Built from
    /// the snapshots' full bounds, not their chunk footprint: a map-spanning edit has to reach every
    /// chunk it might have changed, even though it owns none of them.
    /// </summary>
    private static Dictionary<MapId, (HashSet<ChunkCoord> Chunks, Aabb Region)> TouchedByMap(
        EditSession session,
        Func<IEntity, bool> wasCommitted,
        Func<MapId, LandscapeGrid?> gridFor)
    {
        var byMap = new Dictionary<MapId, (HashSet<ChunkCoord> Chunks, Aabb Region)>();
        foreach ((_, ChunkChangeSnapshot? before, ChunkChangeSnapshot? after) in ReduceImpacts(session.History.UndoStack, wasCommitted))
        {
            Touch(before, byMap, gridFor);
            Touch(after, byMap, gridFor);
        }

        return byMap;
    }

    /// <summary>
    /// Folds a session's commands into one before/after pair per entity and drops the pairs that are
    /// equal — a move and a move back, or any other edit that nets out to nothing, stamps no chunk.
    ///
    /// This fingerprint comparison is the only content comparison in the system. Everything downstream
    /// works from "edited after T", so if this stops discarding no-op edits nothing else will.
    ///
    /// The commit gate is on the command's target, not each impact's entity: a command that edits a
    /// shared resource (a procedural model, a paint image) reports impacts for the placements it fans
    /// out to, and those are never pinned — only the resource is.
    /// </summary>
    internal static IReadOnlyList<(SceneEntity Entity, ChunkChangeSnapshot? Before, ChunkChangeSnapshot? After)> ReduceImpacts(
        IEnumerable<IEditCommand> commands,
        Func<IEntity, bool> wasCommitted)
    {
        var byEntity = new Dictionary<SceneEntity, (ChunkChangeSnapshot? Before, ChunkChangeSnapshot? After)>();
        foreach (IEditCommand command in commands)
        {
            if (command is not IChunkChangeCommand chunkCommand || !command.Targets.Any(wasCommitted))
            {
                continue;
            }

            foreach (ChunkChangeImpact impact in chunkCommand.ChunkImpacts)
            {
                if (!byEntity.TryGetValue(impact.Entity, out (ChunkChangeSnapshot? Before, ChunkChangeSnapshot? After) range))
                {
                    range.Before = impact.Before;
                }

                range.After = impact.After;
                byEntity[impact.Entity] = range;
            }
        }

        return byEntity
            .Where(entry => !Equals(entry.Value.Before, entry.Value.After))
            .Select(entry => (entry.Key, entry.Value.Before, entry.Value.After))
            .ToList();
    }

    private static void Touch(
        ChunkChangeSnapshot? snapshot,
        Dictionary<MapId, (HashSet<ChunkCoord> Chunks, Aabb Region)> byMap,
        Func<MapId, LandscapeGrid?> gridFor)
    {
        if (snapshot == null || gridFor(snapshot.Map) is not { } grid)
        {
            return;
        }

        if (!byMap.TryGetValue(snapshot.Map, out (HashSet<ChunkCoord> Chunks, Aabb Region) entry))
        {
            entry = ([], snapshot.Bounds);
        }

        foreach (ChunkCoord coord in grid.Overlapping(snapshot.Bounds))
        {
            entry.Chunks.Add(coord);
        }

        byMap[snapshot.Map] = (entry.Chunks, entry.Region.Merge(snapshot.Bounds));
    }
}
