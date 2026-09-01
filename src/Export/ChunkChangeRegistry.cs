using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

public sealed class ChunkChangeRegistry
{
    private readonly EditorContext _context;

    public ChunkChangeRegistry(EditorContext context)
    {
        _context = context;
    }

    public void RecordCommit(EditSession session, Func<IEntity, bool> wasCommitted)
    {
        HashSet<(int Map, int X, int Y)> chunks = AffectedChunks(session, wasCommitted);
        if (chunks.Count == 0 || EditorStorage() is not { } storage)
        {
            return;
        }

        string hash = Guid.NewGuid().ToString("N");
        BlockingWork.Run(() => storage.UpsertChunkChangesAsync(chunks, hash));
    }

    public IReadOnlyList<ChunkChange> DirtyFor(string profileId, ChunkExportScope scope)
    {
        if (EditorStorage() is not { } storage)
        {
            return [];
        }

        int? map = scope == ChunkExportScope.CurrentMap ? _context.Maps.CurrentMap.Value : null;
        return BlockingWork.Run(() => storage.LoadDirtyChunksAsync(profileId, map));
    }

    /// <summary>Every chunk in a range export's target rectangle, regardless of dirty status.</summary>
    public IReadOnlyList<ChunkChange> ForRange(ChunkRange range)
    {
        if (EditorStorage() is not { } storage)
        {
            return [];
        }

        return BlockingWork.Run(() => storage.LoadChunksInRangeAsync(range.Map, range.Min, range.Max));
    }

    public void MarkExported(string profileId, IReadOnlyList<ChunkChange> chunks)
    {
        if (chunks.Count == 0 || EditorStorage() is not { } storage)
        {
            return;
        }

        BlockingWork.Run(() => storage.UpsertExportedChunksAsync(profileId, chunks));
    }

    /// <summary>Force-redirties a profile's exported state for a scope, so the next export re-does
    /// everything in it even though content hasn't changed.</summary>
    public void ClearExported(string profileId, ChunkExportScope scope)
    {
        if (EditorStorage() is not { } storage)
        {
            return;
        }

        int? map = scope == ChunkExportScope.CurrentMap ? _context.Maps.CurrentMap.Value : null;
        BlockingWork.Run(() => storage.ClearExportedChunksAsync(profileId, map, null, null));
    }

    /// <summary>Force-redirties a profile's exported state for an explicit chunk range.</summary>
    public void ClearExported(string profileId, ChunkRange range)
    {
        if (EditorStorage() is not { } storage)
        {
            return;
        }

        BlockingWork.Run(() => storage.ClearExportedChunksAsync(
            profileId,
            range.Map.Value,
            (range.Min.X, range.Min.Y),
            (range.Max.X, range.Max.Y)));
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
        if (snapshot == null || _context.Exports.LoadLandscapeSettings(snapshot.Map) is not { } settings)
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
