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
    public IEnumerable<ISceneComponentPersistence> ComponentPersistence => Facet<ISceneComponentPersistence>();

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

    public override Task CommitAsync(IReadOnlyList<IEntity> saves, IReadOnlyList<IEntity> deletes) =>
        CommitAsync(CreateContext, saves, deletes);

    /// <summary>Stamps every chunk a commit touched with the current time.</summary>
    public async Task UpsertChunkChangesAsync(IReadOnlyCollection<(int Map, int X, int Y)> chunks)
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
                    LastEditedUtc = now,
                });
            }
            else
            {
                record.LastEditedUtc = now;
            }
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>Chunks edited strictly after <paramref name="since"/>, optionally on one map.</summary>
    public async Task<IReadOnlyList<ChunkChange>> LoadChangedSinceAsync(DateTime since, int? map)
    {
        using IDisposable reader = await Lock.ReaderAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        IQueryable<ChunkChangeRecord> query = context.ChunkChanges.AsNoTracking()
            .Where(change => change.LastEditedUtc > since);

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

    /// <summary>The newest edit time on record, optionally for one map — the value a consumer stores
    /// as its watermark when nothing changed. Null when nothing has ever been edited.</summary>
    public async Task<DateTime?> LoadLatestEditUtcAsync(int? map)
    {
        using IDisposable reader = await Lock.ReaderAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        IQueryable<ChunkChangeRecord> query = context.ChunkChanges.AsNoTracking();
        if (map is { } mapId)
        {
            query = query.Where(change => change.MapId == mapId);
        }

        return await query.MaxAsync(change => (DateTime?)change.LastEditedUtc).ConfigureAwait(false);
    }

    /// <summary>Every edited chunk in a coordinate rectangle. A chunk no edit has ever touched has no
    /// <see cref="ChunkChangeRecord"/> row and so isn't reported.</summary>
    public async Task<IReadOnlyList<ChunkChange>> LoadChunksInRangeAsync(MapId map, ChunkCoord min, ChunkCoord max)
    {
        using IDisposable reader = await Lock.ReaderAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        List<ChunkChangeRecord> rows = await context.ChunkChanges.AsNoTracking()
            .Where(change => change.MapId == map.Value
                && change.ChunkX >= min.X && change.ChunkX <= max.X
                && change.ChunkY >= min.Y && change.ChunkY <= max.Y)
            .OrderBy(change => change.ChunkY)
            .ThenBy(change => change.ChunkX)
            .ToListAsync()
            .ConfigureAwait(false);

        return rows.Select(ToChange).ToList();
    }

    private static ChunkChange ToChange(ChunkChangeRecord record) =>
        new(new MapId(record.MapId), new ChunkCoord(record.ChunkX, record.ChunkY), record.LastEditedUtc);

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
