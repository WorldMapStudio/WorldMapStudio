using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

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

    // How many rows go into one INSERT … ON DUPLICATE KEY UPDATE. A whole-map commit stamps tens of
    // thousands of chunks; at three parameters a row this stays far under the wire-protocol limit.
    private const int ChunkChangeRowsPerStatement = 1000;

    /// <summary>
    /// Stamps every chunk a commit touched with the current time. One batched upsert per
    /// <see cref="ChunkChangeRowsPerStatement"/> rows: the earlier read-then-write per chunk was a
    /// round-trip apiece, minutes of latency for a full-map edit.
    /// </summary>
    public async Task UpsertChunkChangesAsync(IReadOnlyCollection<(int Map, int X, int Y)> chunks)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        var clock = Stopwatch.StartNew();
        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        (string tableName, string mapColumn, string xColumn, string yColumn, string timeColumn) =
            ChunkChangeColumns(context);

        DbConnection connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync().ConfigureAwait(false);

        DateTime now = DateTime.UtcNow;
        List<(int Map, int X, int Y)> all = chunks.ToList();

        for (int start = 0; start < all.Count; start += ChunkChangeRowsPerStatement)
        {
            int count = Math.Min(ChunkChangeRowsPerStatement, all.Count - start);
            await using DbCommand command = connection.CreateCommand();
            command.CommandTimeout = 60;

            var sql = new StringBuilder(
                $"INSERT INTO `{tableName}` (`{mapColumn}`, `{xColumn}`, `{yColumn}`, `{timeColumn}`) VALUES ");
            for (int i = 0; i < count; i++)
            {
                (int map, int x, int y) = all[start + i];
                sql.Append(i == 0 ? "(" : ",(").Append($"@m{i},@x{i},@y{i},@t)");
                AddParameter(command, $"@m{i}", map);
                AddParameter(command, $"@x{i}", x);
                AddParameter(command, $"@y{i}", y);
            }

            AddParameter(command, "@t", now);
            sql.Append($" ON DUPLICATE KEY UPDATE `{timeColumn}` = VALUES(`{timeColumn}`)");
            command.CommandText = sql.ToString();
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        GD.Print($"[ChunkChanges] Upserted {all.Count} chunk(s) in {clock.ElapsedMilliseconds}ms.");
    }

    /// <summary>
    /// Drops the rows for chunks nothing occupies any more — their absence is what tells a consumer the
    /// map no longer has them, so this is not a cleanup but half of what a commit means. See
    /// <see cref="ChunkChangeLog.RecordCommit"/>.
    ///
    /// Batched like the upsert, and for the same reason. Most of these delete nothing: a commit
    /// reconciles every chunk it could have changed, and a map-spanning edit reaches far more chunks
    /// than the map actually has rows for.
    /// </summary>
    public async Task RemoveChunkChangesAsync(IReadOnlyCollection<(int Map, int X, int Y)> chunks)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        var clock = Stopwatch.StartNew();
        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        (string tableName, string mapColumn, string xColumn, string yColumn, _) = ChunkChangeColumns(context);

        DbConnection connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync().ConfigureAwait(false);

        List<(int Map, int X, int Y)> all = chunks.ToList();

        for (int start = 0; start < all.Count; start += ChunkChangeRowsPerStatement)
        {
            int count = Math.Min(ChunkChangeRowsPerStatement, all.Count - start);
            await using DbCommand command = connection.CreateCommand();
            command.CommandTimeout = 60;

            var sql = new StringBuilder(
                $"DELETE FROM `{tableName}` WHERE (`{mapColumn}`, `{xColumn}`, `{yColumn}`) IN (");
            for (int i = 0; i < count; i++)
            {
                (int map, int x, int y) = all[start + i];
                sql.Append(i == 0 ? "(" : ",(").Append($"@m{i},@x{i},@y{i})");
                AddParameter(command, $"@m{i}", map);
                AddParameter(command, $"@x{i}", x);
                AddParameter(command, $"@y{i}", y);
            }

            sql.Append(')');
            command.CommandText = sql.ToString();
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        GD.Print($"[ChunkChanges] Removed up to {all.Count} chunk(s) in {clock.ElapsedMilliseconds}ms.");
    }

    /// <summary>
    /// Stamps every chunk row the map already has with the current time. A global edit — landscape
    /// settings, a catalog channel/layer/material — can move any chunk's built output without touching
    /// an entity, so nothing feeds it through <see cref="ChunkChangeLog.RecordCommit"/>. One UPDATE,
    /// and it creates no rows: a chunk with no row has nothing there to re-export.
    /// </summary>
    public async Task TouchAllChunkChangesAsync(int mapId)
    {
        var clock = Stopwatch.StartNew();
        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        (string tableName, string mapColumn, _, _, string timeColumn) = ChunkChangeColumns(context);

        DbConnection connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync().ConfigureAwait(false);

        await using DbCommand command = connection.CreateCommand();
        command.CommandTimeout = 60;
        command.CommandText = $"UPDATE `{tableName}` SET `{timeColumn}` = @t WHERE `{mapColumn}` = @m";
        AddParameter(command, "@t", DateTime.UtcNow);
        AddParameter(command, "@m", mapId);
        int touched = await command.ExecuteNonQueryAsync().ConfigureAwait(false);

        GD.Print($"[ChunkChanges] Touched {touched} chunk(s) on map {mapId} in {clock.ElapsedMilliseconds}ms.");
    }

    /// <summary>
    /// Map and last-committed world bounds of every stored scene entity whose component references the
    /// shared resource <paramref name="resourceRecordId"/> of type <paramref name="resourceType"/>,
    /// via whichever <see cref="IResourceReferencingPersistence"/> owns that type. Empty when none
    /// does. See <see cref="ChunkChangeLog.RecordCommit"/>: an edit to a resource has to restamp
    /// placements that were never loaded to be snapshotted.
    /// </summary>
    public async Task<IReadOnlyList<(int EntityId, MapId Map, Aabb Bounds)>> ReferencingPlacementBoundsAsync(
        Type resourceType,
        int resourceRecordId)
    {
        IResourceReferencingPersistence? persistence = ComponentPersistence
            .OfType<IResourceReferencingPersistence>()
            .FirstOrDefault(candidate => candidate.ReferencedResourceType == resourceType);
        if (persistence == null)
        {
            return [];
        }

        using IDisposable read = await Lock.ReaderAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();
        return await persistence.ReferencingBoundsAsync(context, resourceRecordId).ConfigureAwait(false);
    }

    /// <summary>Table and column names off the model, so a naming convention can never silently desync
    /// the hand-written chunk-change SQL from what EF maps the record to.</summary>
    private static (string Table, string Map, string X, string Y, string Time) ChunkChangeColumns(EditorDbContext context)
    {
        IEntityType type = context.Model.FindEntityType(typeof(ChunkChangeRecord))!;
        var table = StoreObjectIdentifier.Table(type.GetTableName()!, type.GetSchema());
        string Column(string property) => type.FindProperty(property)!.GetColumnName(table)!;

        return (
            type.GetTableName()!,
            Column(nameof(ChunkChangeRecord.MapId)),
            Column(nameof(ChunkChangeRecord.ChunkX)),
            Column(nameof(ChunkChangeRecord.ChunkY)),
            Column(nameof(ChunkChangeRecord.LastEditedUtc)));
    }

    // How many rows go into one INSERT … ON DUPLICATE KEY UPDATE here. Two bigint columns, so this
    // sits above the chunk-change batch size and well under the wire-protocol limit.
    private const int LongPairRowsPerStatement = 2000;

    /// <summary>
    /// Batched upsert for a table keyed on one <see langword="long"/> column with one
    /// <see langword="long"/> value column — the shape a caller-owned ledger record takes when all it
    /// needs is "assign this value to this key". One INSERT … ON DUPLICATE KEY UPDATE per
    /// <see cref="LongPairRowsPerStatement"/> rows instead of a read-then-write round trip per pair.
    /// <typeparamref name="TRecord"/> stays whatever the caller's own entity type is — this storage
    /// only needs its table and column names, read off <paramref name="context"/>'s model so a naming
    /// convention cannot desync the hand-written SQL from the record.
    /// </summary>
    public async Task UpsertLongPairsAsync<TRecord>(
        IReadOnlyCollection<(long Key, long Value)> pairs,
        string keyPropertyName,
        string valuePropertyName)
        where TRecord : class
    {
        if (pairs.Count == 0)
        {
            return;
        }

        var clock = Stopwatch.StartNew();
        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        (string table, string keyColumn, string valueColumn) =
            LongPairColumns<TRecord>(context, keyPropertyName, valuePropertyName);

        DbConnection connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync().ConfigureAwait(false);

        List<(long Key, long Value)> all = pairs as List<(long, long)> ?? pairs.ToList();

        for (int start = 0; start < all.Count; start += LongPairRowsPerStatement)
        {
            int count = Math.Min(LongPairRowsPerStatement, all.Count - start);
            await using DbCommand command = connection.CreateCommand();
            command.CommandTimeout = 60;

            var sql = new StringBuilder($"INSERT INTO `{table}` (`{keyColumn}`, `{valueColumn}`) VALUES ");
            for (int i = 0; i < count; i++)
            {
                (long key, long value) = all[start + i];
                sql.Append(i == 0 ? "(" : ",(").Append($"@k{i},@v{i})");
                AddParameter(command, $"@k{i}", key);
                AddParameter(command, $"@v{i}", value);
            }

            sql.Append($" ON DUPLICATE KEY UPDATE `{valueColumn}` = VALUES(`{valueColumn}`)");
            command.CommandText = sql.ToString();
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        GD.Print($"[LongPairs] Upserted {all.Count} row(s) into {table} in {clock.ElapsedMilliseconds}ms.");
    }

    private static (string Table, string Key, string Value) LongPairColumns<TRecord>(
        EditorDbContext context,
        string keyPropertyName,
        string valuePropertyName)
        where TRecord : class
    {
        IEntityType type = context.Model.FindEntityType(typeof(TRecord))!;
        var table = StoreObjectIdentifier.Table(type.GetTableName()!, type.GetSchema());
        string Column(string property) => type.FindProperty(property)!.GetColumnName(table)!;

        return (type.GetTableName()!, Column(keyPropertyName), Column(valuePropertyName));
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
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

    /// <summary>
    /// Stored pixels for the wanted chunks, decoded. One query per database-backed image bounded by
    /// that image's own wanted rect rather than a scan of its whole chunk table — an image can hold
    /// far more stored chunks than are ever wanted at once. A disk-backed image
    /// (<see cref="PaintImageStorageKind.Disk"/>) is served from its asset-source files by
    /// <see cref="ImageDiskStore"/> instead. A coord with no stored row / no file simply does not come
    /// back, which the sampler already reads as all-zero.
    /// </summary>
    public async Task<IReadOnlyList<(PaintImage Image, ImageChunkCoord Coord, byte[] Pixels)>> LoadImageChunksAsync(
        IReadOnlyDictionary<PaintImage, IReadOnlyCollection<ImageChunkCoord>> wanted)
    {
        var result = new List<(PaintImage, ImageChunkCoord, byte[])>();
        if (wanted.Count == 0)
        {
            return result;
        }

        var diskStore = new ImageDiskStore();
        foreach ((PaintImage image, IReadOnlyCollection<ImageChunkCoord> coords) in wanted)
        {
            if (coords.Count == 0 || !image.IsDiskBacked)
            {
                continue;
            }

            foreach ((ImageChunkCoord coord, byte[] pixels) in await diskStore.ReadChunksAsync(image, coords).ConfigureAwait(false))
            {
                result.Add((image, coord, pixels));
            }
        }

        if (wanted.All(pair => pair.Key.IsDiskBacked || pair.Value.Count == 0))
        {
            return result;
        }

        using IDisposable read = await Lock.ReaderAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        foreach ((PaintImage image, IReadOnlyCollection<ImageChunkCoord> coords) in wanted)
        {
            if (coords.Count == 0 || image.IsDiskBacked)
            {
                continue;
            }

            int imageId = image.RecordId ?? 0;
            int minX = coords.Min(c => c.X);
            int maxX = coords.Max(c => c.X);
            int minY = coords.Min(c => c.Y);
            int maxY = coords.Max(c => c.Y);
            var set = coords as HashSet<ImageChunkCoord> ?? new HashSet<ImageChunkCoord>(coords);

            List<ImageChunkRecord> rows = await context.ImageChunks.AsNoTracking()
                .Where(row => row.ImageId == imageId
                    && row.ChunkX >= minX && row.ChunkX <= maxX
                    && row.ChunkY >= minY && row.ChunkY <= maxY)
                .ToListAsync().ConfigureAwait(false);

            foreach (ImageChunkRecord row in rows)
            {
                var coord = new ImageChunkCoord(row.ChunkX, row.ChunkY);
                if (!set.Contains(coord))
                {
                    continue;
                }

                byte[] pixels = ImageChunkCodec.Decode(row.Format, row.Pixels, image.ChunkSize, image.Stride);
                result.Add((image, coord, pixels));
            }
        }

        return result;
    }

    // How many chunk rows go into one INSERT … ON DUPLICATE KEY UPDATE here. Each row carries a whole
    // pixel blob, so this is far smaller than the chunk-change batch — it keeps one statement's total
    // byte size sane rather than the parameter count.
    private const int ImageChunkRowsPerStatement = 64;

    /// <summary>
    /// Writes chunk pixels straight to storage for one image, bypassing residency and the dirty
    /// tracking a <see cref="PaintImage"/> keeps — the write counterpart to
    /// <see cref="LoadImageChunksAsync"/>. A bulk producer that never makes its chunks resident (so it
    /// stays bounded in memory across a very large run) writes through here instead of staging them
    /// into a commit. The <see cref="PaintImage"/> is not touched; its manifest catches up on the next
    /// load.
    ///
    /// A disk-backed image goes to tile files through <see cref="ImageDiskStore"/>; a database-backed
    /// one is upserted into the chunk table in batches, each buffer encoded the same way
    /// <see cref="PaintImageFactory"/> encodes a staged chunk.
    /// </summary>
    public Task UpsertImageChunksAsync(PaintImage image, IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)> chunks) =>
        UpsertImageChunksAsync(new Dictionary<PaintImage, IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)>> { [image] = chunks });

    /// <summary>
    /// The same upsert as the single-image overload, but every database-backed image in
    /// <paramref name="writes"/> lands through one context, one lock acquisition and one transaction —
    /// a bulk producer touching several images for what is conceptually one unit of work (a tile, a
    /// region) pays for one round trip instead of one per image.
    /// </summary>
    public async Task UpsertImageChunksAsync(
        IReadOnlyDictionary<PaintImage, IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)>> writes)
    {
        foreach ((PaintImage image, IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)> chunks) in writes)
        {
            if (chunks.Count > 0 && image.IsDiskBacked)
            {
                await new ImageDiskStore().WriteChunksAsync(image, chunks, Array.Empty<ImageChunkCoord>()).ConfigureAwait(false);
            }
        }

        List<(PaintImage Image, IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)> Chunks)> databaseWrites = writes
            .Where(pair => pair.Value.Count > 0 && !pair.Key.IsDiskBacked)
            .Select(pair => (pair.Key, pair.Value))
            .ToList();
        if (databaseWrites.Count == 0)
        {
            return;
        }

        var clock = Stopwatch.StartNew();
        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        (string table, string idColumn, string xColumn, string yColumn, string formatColumn, string pixelsColumn) =
            ImageChunkColumns(context);

        DbConnection connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync().ConfigureAwait(false);

        int totalChunks = 0;
        foreach ((PaintImage image, IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)> chunks) in databaseWrites)
        {
            int imageId = image.RecordId
                ?? throw new InvalidOperationException("Image has no record id — its header must be saved before chunks are written.");

            for (int start = 0; start < chunks.Count; start += ImageChunkRowsPerStatement)
            {
                int count = Math.Min(ImageChunkRowsPerStatement, chunks.Count - start);
                await using DbCommand command = connection.CreateCommand();
                command.CommandTimeout = 120;

                var sql = new StringBuilder(
                    $"INSERT INTO `{table}` (`{idColumn}`, `{xColumn}`, `{yColumn}`, `{formatColumn}`, `{pixelsColumn}`) VALUES ");
                for (int i = 0; i < count; i++)
                {
                    (ImageChunkCoord coord, byte[] pixels) = chunks[start + i];
                    (byte format, byte[] bytes) = ImageChunkCodec.Encode(pixels);
                    sql.Append(i == 0 ? "(" : ",(").Append($"@i{i},@x{i},@y{i},@f{i},@p{i})");
                    AddParameter(command, $"@i{i}", imageId);
                    AddParameter(command, $"@x{i}", coord.X);
                    AddParameter(command, $"@y{i}", coord.Y);
                    AddParameter(command, $"@f{i}", format);
                    AddParameter(command, $"@p{i}", bytes);
                }

                sql.Append($" ON DUPLICATE KEY UPDATE `{formatColumn}` = VALUES(`{formatColumn}`), `{pixelsColumn}` = VALUES(`{pixelsColumn}`)");
                command.CommandText = sql.ToString();
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            totalChunks += chunks.Count;
        }

        GD.Print($"[ImageChunks] Upserted {totalChunks} chunk(s) across {databaseWrites.Count} image(s) in {clock.ElapsedMilliseconds}ms.");
    }

    private static (string Table, string Id, string X, string Y, string Format, string Pixels) ImageChunkColumns(EditorDbContext context)
    {
        IEntityType type = context.Model.FindEntityType(typeof(ImageChunkRecord))!;
        var table = StoreObjectIdentifier.Table(type.GetTableName()!, type.GetSchema());
        string Column(string property) => type.FindProperty(property)!.GetColumnName(table)!;

        return (
            type.GetTableName()!,
            Column(nameof(ImageChunkRecord.ImageId)),
            Column(nameof(ImageChunkRecord.ChunkX)),
            Column(nameof(ImageChunkRecord.ChunkY)),
            Column(nameof(ImageChunkRecord.Format)),
            Column(nameof(ImageChunkRecord.Pixels)));
    }

    public async Task<string?> LoadBatchStateAsync(string operationId, string key)
    {
        using IDisposable reader = await Lock.ReaderAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        BatchStateRecord? record = await context.BatchState.AsNoTracking()
            .FirstOrDefaultAsync(row => row.OperationId == operationId && row.Key == key)
            .ConfigureAwait(false);

        return record?.Value;
    }

    public async Task<IReadOnlyDictionary<string, string>> LoadAllBatchStateAsync(string operationId)
    {
        using IDisposable reader = await Lock.ReaderAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        List<BatchStateRecord> rows = await context.BatchState.AsNoTracking()
            .Where(row => row.OperationId == operationId)
            .ToListAsync()
            .ConfigureAwait(false);

        return rows.ToDictionary(row => row.Key, row => row.Value);
    }

    public async Task UpsertBatchStateAsync(string operationId, string key, string value)
    {
        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        BatchStateRecord? record = await context.BatchState.FindAsync([operationId, key]).ConfigureAwait(false);
        if (record == null)
        {
            context.BatchState.Add(new BatchStateRecord
            {
                OperationId = operationId,
                Key = key,
                Value = value,
                UpdatedAtUtc = DateTime.UtcNow,
            });
        }
        else
        {
            record.Value = value;
            record.UpdatedAtUtc = DateTime.UtcNow;
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task RemoveBatchStateAsync(string operationId, string key)
    {
        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();

        if (await context.BatchState.FindAsync([operationId, key]).ConfigureAwait(false) is { } record)
        {
            context.BatchState.Remove(record);
            await context.SaveChangesAsync().ConfigureAwait(false);
        }
    }
}
