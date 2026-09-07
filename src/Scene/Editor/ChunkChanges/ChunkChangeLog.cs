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
    /// Blocking rather than async because it runs inside the commit path on the main thread, and the
    /// rest of that path is synchronous.
    /// </summary>
    public void RecordCommit(EditSession session, Func<IEntity, bool> wasCommitted)
    {
        if (EditorStorage() is not { } storage)
        {
            return;
        }

        foreach ((MapId map, (HashSet<ChunkCoord> touched, Aabb region)) in TouchedByMap(session, wasCommitted))
        {
            HashSet<ChunkCoord> occupied = OccupiedChunks(map, region);

            List<(int Map, int X, int Y)> present = [];
            List<(int Map, int X, int Y)> vacated = [];
            foreach (ChunkCoord coord in touched)
            {
                (occupied.Contains(coord) ? present : vacated).Add((map.Value, coord.X, coord.Y));
            }

            BlockingWork.Run(() => storage.UpsertChunkChangesAsync(present));
            BlockingWork.Run(() => storage.RemoveChunkChangesAsync(vacated));
        }
    }

    /// <summary>Every chunk edited strictly after <paramref name="since"/>, optionally on one map.
    /// The one query the whole caching model is built on.</summary>
    public async Task<IReadOnlyList<ChunkChange>> ChangedSinceAsync(DateTime since, MapId? map = null)
    {
        if (EditorStorage() is not { } storage)
        {
            return [];
        }

        return await storage.LoadChangedSinceAsync(since, map?.Value).ConfigureAwait(false);
    }

    /// <summary>Every chunk a map still has, with when each was last edited. What a consumer
    /// reconciles its own output against to notice what has been removed.</summary>
    public Task<IReadOnlyList<ChunkChange>> ExistingAsync(MapId? map = null) =>
        ChangedSinceAsync(DateTime.MinValue, map);

    /// <summary>Every chunk in a coordinate rectangle that the map still has.</summary>
    public async Task<IReadOnlyList<ChunkChange>> InRangeAsync(ChunkRange range)
    {
        if (EditorStorage() is not { } storage)
        {
            return [];
        }

        return await storage.LoadChunksInRangeAsync(range.Map, range.Min, range.Max).ConfigureAwait(false);
    }

    /// <summary>The newest edit time on record, so a consumer whose query returned nothing can still
    /// move its watermark forward. Null when nothing has ever been edited.</summary>
    public async Task<DateTime?> LatestEditUtcAsync(MapId? map = null)
    {
        if (EditorStorage() is not { } storage)
        {
            return null;
        }

        return await storage.LoadLatestEditUtcAsync(map?.Value).ConfigureAwait(false);
    }

    /// <summary>
    /// Per map, the chunks this commit could have changed and the world region they span. Built from
    /// the snapshots' full bounds, not their chunk footprint: a map-spanning edit has to reach every
    /// chunk it might have changed, even though it owns none of them.
    /// </summary>
    private Dictionary<MapId, (HashSet<ChunkCoord> Chunks, Aabb Region)> TouchedByMap(
        EditSession session,
        Func<IEntity, bool> wasCommitted)
    {
        var byMap = new Dictionary<MapId, (HashSet<ChunkCoord> Chunks, Aabb Region)>();
        foreach ((_, ChunkChangeSnapshot? before, ChunkChangeSnapshot? after) in ReduceImpacts(session.History.UndoStack, wasCommitted))
        {
            Touch(before, byMap);
            Touch(after, byMap);
        }

        return byMap;
    }

    /// <summary>
    /// Which chunks in <paramref name="region"/> something still sits on, read back from storage after
    /// the commit has landed. Entities are scanned by their full bounds — that is what the database
    /// indexes — and then filtered by <see cref="SceneEntity.WorldChunkBounds"/>, so a global light is
    /// returned by the scan and still claims nothing.
    /// </summary>
    private HashSet<ChunkCoord> OccupiedChunks(MapId map, Aabb region)
    {
        var occupied = new HashSet<ChunkCoord>();
        if (_context.Landscape.LoadSettingsFor(map) is not { } settings)
        {
            return occupied;
        }

        var grid = new LandscapeGrid(settings);
        IReadOnlyList<SceneEntity> entities = BlockingWork.Run(() => _context.Database.ScanSceneAsync(map, region));

        foreach (SceneEntity entity in entities)
        {
            if (entity.WorldChunkBounds is not { } bounds)
            {
                continue;
            }

            foreach (ChunkCoord coord in grid.Overlapping(bounds))
            {
                occupied.Add(coord);
            }
        }

        return occupied;
    }

    /// <summary>
    /// Folds a session's commands into one before/after pair per entity and drops the pairs that are
    /// equal — a move and a move back, or any other edit that nets out to nothing, stamps no chunk.
    ///
    /// This fingerprint comparison is the only content comparison in the system. Everything downstream
    /// works from "edited after T", so if this stops discarding no-op edits nothing else will.
    /// </summary>
    internal static IReadOnlyList<(SceneEntity Entity, ChunkChangeSnapshot? Before, ChunkChangeSnapshot? After)> ReduceImpacts(
        IEnumerable<IEditCommand> commands,
        Func<IEntity, bool> wasCommitted)
    {
        var byEntity = new Dictionary<SceneEntity, (ChunkChangeSnapshot? Before, ChunkChangeSnapshot? After)>();
        foreach (IEditCommand command in commands)
        {
            if (command is not IChunkChangeCommand chunkCommand)
            {
                continue;
            }

            foreach (ChunkChangeImpact impact in chunkCommand.ChunkImpacts)
            {
                if (!wasCommitted(impact.Entity))
                {
                    continue;
                }

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

    private void Touch(ChunkChangeSnapshot? snapshot, Dictionary<MapId, (HashSet<ChunkCoord> Chunks, Aabb Region)> byMap)
    {
        if (snapshot == null || _context.Landscape.LoadSettingsFor(snapshot.Map) is not { } settings)
        {
            return;
        }

        if (!byMap.TryGetValue(snapshot.Map, out (HashSet<ChunkCoord> Chunks, Aabb Region) entry))
        {
            entry = ([], snapshot.Bounds);
        }

        var grid = new LandscapeGrid(settings);
        foreach (ChunkCoord coord in grid.Overlapping(snapshot.Bounds))
        {
            entry.Chunks.Add(coord);
        }

        byMap[snapshot.Map] = (entry.Chunks, entry.Region.Merge(snapshot.Bounds));
    }

    private EditorStorage? EditorStorage() => _context.Database.Storages.OfType<EditorStorage>().FirstOrDefault();
}
