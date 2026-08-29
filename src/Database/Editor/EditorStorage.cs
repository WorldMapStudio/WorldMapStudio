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

    public override IEnumerable<ISceneEntityFactory> SceneFactories => Subsystems.OfType<ISceneEntityFactory>();

    public override IEnumerable<ICatalogEntityFactory> CatalogFactories => Subsystems.OfType<ICatalogEntityFactory>();

    public override IEnumerable<IMapSource> MapSources => Subsystems.OfType<IMapSource>();

    public override IEnumerable<ILandscapeSettingsSource> LandscapeSettingsSources => Subsystems.OfType<ILandscapeSettingsSource>();

    /// <summary>Registered scene-component persisters, so a plugin's component is stored the same way
    /// a built-in one is. See <see cref="ISceneComponentPersistence"/>.</summary>
    public IEnumerable<ISceneComponentPersistence> ComponentPersistence => Subsystems.OfType<ISceneComponentPersistence>();

    /// <summary>Opens a short-lived context for one unit of work against this storage.</summary>
    public EditorDbContext CreateContext() =>
        new(BuildOptions<EditorDbContext>(), ComponentPersistence.ToList(), EntityFactories.ToList());

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

    public async Task<IReadOnlyList<ChunkChange>> LoadDirtyChunksAsync(string exporterId, int? map)
    {
        using IDisposable reader = await Lock.ReaderAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        var query =
            from change in context.ChunkChanges.AsNoTracking()
            join exported in context.ExportedChunks.AsNoTracking().Where(record => record.ExporterId == exporterId)
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

    public async Task UpsertExportedChunksAsync(string exporterId, IReadOnlyList<ChunkChange> chunks)
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
            object[] key = [exporterId, chunk.Map.Value, chunk.Coord.X, chunk.Coord.Y];
            ExportedChunkRecord? record = await context.ExportedChunks.FindAsync(key).ConfigureAwait(false);
            if (record == null)
            {
                context.ExportedChunks.Add(new ExportedChunkRecord
                {
                    ExporterId = exporterId,
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

    private static ChunkChange ToChange(ChunkChangeRecord record) =>
        new(new MapId(record.MapId), new ChunkCoord(record.ChunkX, record.ChunkY), record.ContentHash);
}
