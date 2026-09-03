using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>
/// The built-in default storage that manages the editor's own entities. Users extend it with new
/// tables and entities (by registering factories into it) or define their own separate storages.
/// Hosts its entity factories, which self-register with [Subsystem(nameof(EditorStorage))].
/// </summary>
[Subsystem(nameof(DatabaseSystem))]
public sealed partial class EditorStorage : Storage, ISubsystemHost
{
    public const string StorageName = "Editor";
    private readonly DatabaseSystem _database;

    public override string Name => StorageName;

    protected override IEnumerable<ISubsystem> HostedSubsystems => Subsystems;

    public EditorStorage(DatabaseSystem database)
    {
        _database = database;
        InitializeSubsystems();
    }

    public AssetSystem Assets => _database.Context.Assets;

    public MeshMaterialSystem MeshMaterials => _database.Context.MeshMaterials;

    public EditorContext Context => _database.Context;

    // Default to an editor-managed dolt instance so a new project works out of the box. Exposed
    // statically so project settings (created before any Storage instance exists) can seed the same
    // defaults.
    public static StorageConnection DefaultConnection() => new()
    {
        LaunchServer = true,
        Port = 3312,
        Database = "editor",
    };

    public override StorageConnection CreateDefaultConnection() => DefaultConnection();

    /// <summary>Registered scene-component persisters, so a plugin's component is stored the same way
    /// a built-in one is. See <see cref="ISceneComponentPersistence"/>.</summary>
    public IEnumerable<ISceneComponentPersistence> ComponentPersistence => Subsystems.OfType<ISceneComponentPersistence>();

    /// <summary>Opens a short-lived context for one unit of work against this storage.</summary>
    public EditorDbContext CreateContext() =>
        new(BuildOptions<EditorDbContext>(), ComponentPersistence.ToList(), EntityFactories.ToList(), TableConfigurations.ToList());

    public override void EnsureSchema()
    {
        using EditorDbContext context = CreateContext();
        context.Database.EnsureCreated();
    }

    public override Schema? ExpectedSchema()
    {
        using EditorDbContext context = CreateContext();
        return ModelSchema.Extract(context);
    }

    public override async Task CommitAsync(IReadOnlyList<IEntity> saves, IReadOnlyList<IEntity> deletes)
    {
        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        var writeBacks = new List<Action>();
        foreach (IEntity entity in saves)
        {
            if (FactoryFor(entity) is { } factory)
            {
                writeBacks.Add(factory.Stage(context, entity));
            }
        }

        foreach (IEntity entity in deletes)
        {
            FactoryFor(entity)?.StageDelete(context, entity);
        }

        // A single SaveChanges wraps all staged inserts/updates/deletes in one transaction.
        await context.SaveChangesAsync().ConfigureAwait(false);

        foreach (Action writeBack in writeBacks)
        {
            writeBack();
        }
    }

    public async Task UpsertChunkChangesAsync(IReadOnlyCollection<(int Map, int X, int Y)> chunks, string contentHash)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();
        DateTime now = DateTime.UtcNow;

        foreach ((int map, int x, int y) in chunks)
        {
            ChunkChangeRecord? record = await context.ChunkChanges.FindAsync([map, x, y]).ConfigureAwait(false);
            if (record == null)
            {
                context.ChunkChanges.Add(new ChunkChangeRecord
                {
                    MapId = map,
                    ChunkX = x,
                    ChunkY = y,
                    ContentHash = contentHash,
                    UpdatedAtUtc = now,
                });
            }
            else
            {
                record.ContentHash = contentHash;
                record.UpdatedAtUtc = now;
            }
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChunkChange>> LoadDirtyChunksAsync(string profileId, int? map)
    {
        using IDisposable reader = await Lock.ReaderAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        var query =
            from change in context.ChunkChanges.AsNoTracking()
            join exported in context.ExportedChunks.AsNoTracking().Where(record => record.ProfileId == profileId)
                on new { change.MapId, change.ChunkX, change.ChunkY }
                equals new { exported.MapId, exported.ChunkX, exported.ChunkY }
                into exportedJoin
            from exported in exportedJoin.DefaultIfEmpty()
            where exported == null || exported.ContentHash != change.ContentHash
            select change;

        if (map is { } mapId)
        {
            query = query.Where(change => change.MapId == mapId);
        }

        List<ChunkChangeRecord> rows = await query
            .OrderBy(change => change.MapId)
            .ThenBy(change => change.ChunkY)
            .ThenBy(change => change.ChunkX)
            .ToListAsync()
            .ConfigureAwait(false);

        return rows.Select(ToChange).ToList();
    }

    /// <summary>Every chunk in a coordinate rectangle, regardless of dirty status — the source for a
    /// range export. A chunk never touched by an edit has no <see cref="ChunkChangeRecord"/> row, so
    /// it reports an empty content hash; that's fine, since it only starts looking dirty once a real
    /// edit inserts a row with an actual hash.</summary>
    public async Task<IReadOnlyList<ChunkChange>> LoadChunksInRangeAsync(MapId map, ChunkCoord min, ChunkCoord max)
    {
        using IDisposable reader = await Lock.ReaderAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        List<ChunkChangeRecord> rows = await context.ChunkChanges.AsNoTracking()
            .Where(change => change.MapId == map.Value
                && change.ChunkX >= min.X && change.ChunkX <= max.X
                && change.ChunkY >= min.Y && change.ChunkY <= max.Y)
            .ToListAsync()
            .ConfigureAwait(false);

        Dictionary<(int X, int Y), string> hashes = rows.ToDictionary(row => (row.ChunkX, row.ChunkY), row => row.ContentHash);

        var result = new List<ChunkChange>();
        for (int y = min.Y; y <= max.Y; y++)
        {
            for (int x = min.X; x <= max.X; x++)
            {
                result.Add(new ChunkChange(map, new ChunkCoord(x, y), hashes.GetValueOrDefault((x, y), "")));
            }
        }

        return result;
    }

    public async Task UpsertExportedChunksAsync(string profileId, IReadOnlyList<ChunkChange> chunks)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();
        DateTime now = DateTime.UtcNow;

        foreach (ChunkChange chunk in chunks)
        {
            object[] key = [profileId, chunk.Map.Value, chunk.Coord.X, chunk.Coord.Y];
            ExportedChunkRecord? record = await context.ExportedChunks.FindAsync(key).ConfigureAwait(false);
            if (record == null)
            {
                context.ExportedChunks.Add(new ExportedChunkRecord
                {
                    ProfileId = profileId,
                    MapId = chunk.Map.Value,
                    ChunkX = chunk.Coord.X,
                    ChunkY = chunk.Coord.Y,
                    ContentHash = chunk.ContentHash,
                    ExportedAtUtc = now,
                });
            }
            else
            {
                record.ContentHash = chunk.ContentHash;
                record.ExportedAtUtc = now;
            }
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>Force-redirties a profile's exported state for a scope/range by deleting its tracked
    /// exported-chunk rows, so the next export re-does them even though content hasn't changed.</summary>
    public async Task ClearExportedChunksAsync(string profileId, int? map, (int X, int Y)? min, (int X, int Y)? max)
    {
        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        IQueryable<ExportedChunkRecord> query = context.ExportedChunks.Where(record => record.ProfileId == profileId);

        if (map is { } mapId)
        {
            query = query.Where(record => record.MapId == mapId);
        }

        if (min is { } lo && max is { } hi)
        {
            query = query.Where(record =>
                record.ChunkX >= lo.X && record.ChunkX <= hi.X &&
                record.ChunkY >= lo.Y && record.ChunkY <= hi.Y);
        }

        List<ExportedChunkRecord> rows = await query.ToListAsync().ConfigureAwait(false);
        context.ExportedChunks.RemoveRange(rows);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    private static ChunkChange ToChange(ChunkChangeRecord record) =>
        new(new MapId(record.MapId), new ChunkCoord(record.ChunkX, record.ChunkY), record.ContentHash);

    public async Task<IReadOnlyDictionary<long, long>> LoadExportedEntityIdsAsync(string profileId)
    {
        using IDisposable reader = await Lock.ReaderAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        List<ExportedEntityIdRecord> rows = await context.ExportedEntityIds.AsNoTracking()
            .Where(record => record.ProfileId == profileId)
            .ToListAsync()
            .ConfigureAwait(false);

        return rows.ToDictionary(record => record.EntityId, record => record.AllocatedId);
    }

    public async Task UpsertExportedEntityIdsAsync(string profileId, IReadOnlyDictionary<long, long> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        foreach ((long entityId, long allocatedId) in ids)
        {
            object[] key = [profileId, entityId];
            ExportedEntityIdRecord? record = await context.ExportedEntityIds.FindAsync(key).ConfigureAwait(false);
            if (record == null)
            {
                context.ExportedEntityIds.Add(new ExportedEntityIdRecord
                {
                    ProfileId = profileId,
                    EntityId = entityId,
                    AllocatedId = allocatedId,
                });
            }
            else
            {
                record.AllocatedId = allocatedId;
            }
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
    }
}
