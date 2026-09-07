using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>A chunk and when an edit last touched it.</summary>
public readonly record struct ChunkChange(MapId Map, ChunkCoord Coord, DateTime LastEditedUtc);

/// <summary>
/// When each chunk was last edited, and nothing else. A plain member of <see cref="EditorContext"/>,
/// written by <see cref="DatabaseSystem.Persist"/> on every commit.
///
/// Purely a log: it never learns who read it or what anyone considers up to date. A consumer keeps
/// one watermark per whatever unit it cares about — a map, an output folder, the whole project — and
/// asks <see cref="ChangedSinceAsync"/> what happened after it. That is its entire cache.
/// </summary>
public sealed class ChunkChangeLog
{
    private readonly EditorContext _context;

    public ChunkChangeLog(EditorContext context)
    {
        _context = context;
    }

    /// <summary>Stamps every chunk a commit touched. Blocking rather than async because it runs
    /// inside the commit path on the main thread, and the rest of that path is synchronous.</summary>
    public void RecordCommit(EditSession session, Func<IEntity, bool> wasCommitted)
    {
        HashSet<(int Map, int X, int Y)> chunks = AffectedChunks(session, wasCommitted);
        if (chunks.Count == 0 || EditorStorage() is not { } storage)
        {
            return;
        }

        BlockingWork.Run(() => storage.UpsertChunkChangesAsync(chunks));
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

    /// <summary>Every chunk in a coordinate rectangle that has ever been edited.</summary>
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

    private HashSet<(int Map, int X, int Y)> AffectedChunks(EditSession session, Func<IEntity, bool> wasCommitted)
    {
        var chunks = new HashSet<(int Map, int X, int Y)>();
        foreach ((_, ChunkChangeSnapshot? before, ChunkChangeSnapshot? after) in ReduceImpacts(session.History.UndoStack, wasCommitted))
        {
            Add(before, chunks);
            Add(after, chunks);
        }

        return chunks;
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

    private void Add(ChunkChangeSnapshot? snapshot, HashSet<(int Map, int X, int Y)> chunks)
    {
        if (snapshot == null || _context.Landscape.LoadSettingsFor(snapshot.Map) is not { } settings)
        {
            return;
        }

        var grid = new LandscapeGrid(settings);
        foreach (ChunkCoord coord in grid.Overlapping(snapshot.Bounds))
        {
            chunks.Add((snapshot.Map.Value, coord.X, coord.Y));
        }
    }

    private EditorStorage? EditorStorage() => _context.Database.Storages.OfType<EditorStorage>().FirstOrDefault();
}
