using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>
/// Which externally-stored entities have a <c>wms_entities</c> row: (source, key) to the row's id. Held in
/// memory so a scan can tell whether an entity read from another table carries editor data without asking
/// the database about every one of them.
///
/// Sparse by construction: a bridge row exists only while its entity has tags or attached components (see
/// <see cref="EditorStorage.CommitBridgedAsync"/>), so this holds one entry per tagged creature, not one
/// per spawn. Read from the scan thread through <see cref="Snapshot"/>, an immutable dictionary; written
/// only on the main thread, by the commit write-back, which swaps in a new one.
/// </summary>
public sealed class EntityBridgeIndex(EditorContext context) : IWorldParticipant
{
    private FrozenDictionary<(string Source, long Key), int> _snapshot =
        FrozenDictionary<(string Source, long Key), int>.Empty;

    /// <summary>The bridge rows as of the last load or commit. Immutable, so a scan can hold it.</summary>
    public FrozenDictionary<(string Source, long Key), int> Snapshot => _snapshot;

    public bool TryGet(string source, long key, out int entityId) => _snapshot.TryGetValue((source, key), out entityId);

    /// <summary>Applies a commit's bridge-row inserts and deletes. Main thread only.</summary>
    public void Apply(IReadOnlyCollection<(string Source, long Key, int EntityId)> added, IReadOnlyCollection<(string Source, long Key)> removed)
    {
        if (added.Count == 0 && removed.Count == 0)
        {
            return;
        }

        var next = new Dictionary<(string Source, long Key), int>(_snapshot);
        foreach ((string source, long key) in removed)
        {
            next.Remove((source, key));
        }

        foreach ((string source, long key, int entityId) in added)
        {
            next[(source, key)] = entityId;
        }

        _snapshot = next.ToFrozenDictionary();
    }

    // After the catalogs the tags and components refer to; ties don't matter to anything else.
    float IWorldParticipant.LoadPriority => -0.5f;

    void IWorldParticipant.LoadWorld()
    {
        EditorStorage? storage = context.Database.Storages.OfType<EditorStorage>().FirstOrDefault();
        if (storage == null)
        {
            return;
        }

        try
        {
            List<(int Id, string Source, long Key)> rows = BlockingWork.Run(() => ReadAsync(storage));
            _snapshot = rows.ToFrozenDictionary(row => (row.Source, row.Key), row => row.Id);
        }
        catch (Exception e)
        {
            GD.PushError($"[Bridge] Loading bridged entity rows failed: {e.Message}");
        }
    }

    void IWorldParticipant.UnloadWorld() => _snapshot = FrozenDictionary<(string Source, long Key), int>.Empty;

    private static async Task<List<(int Id, string Source, long Key)>> ReadAsync(EditorStorage storage)
    {
        using IDisposable reader = await storage.Lock.ReaderAsync().ConfigureAwait(false);
        await using EditorDbContext db = storage.CreateContext();
        List<EntityRecord> rows = await db.Entities.AsNoTracking()
            .Where(record => record.Source != null && record.SourceKey != null)
            .ToListAsync()
            .ConfigureAwait(false);
        return rows.Select(row => (row.Id, row.Source!, row.SourceKey!.Value)).ToList();
    }
}
