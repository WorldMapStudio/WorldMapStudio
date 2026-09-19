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

    public EditorStorage(DatabaseSystem database)
    {
        _database = database;
        Attachments = new EntityAttachments(this);
        InitializeSubsystems();
    }

    public AssetSystem Assets => _database.Context.Assets;

    public MeshMaterialSystem MeshMaterials => _database.Context.MeshMaterials;

    public EditorContext Context => _database.Context;

    /// <summary>The id source for every new <c>wms_entities</c> row, native or bridged.</summary>
    public EntityIdAllocator EntityIds { get; } = new();

    /// <summary>Loads and stages the tags and components an entity carries, whichever table owns it.</summary>
    public EntityAttachments Attachments { get; }

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

    /// <summary>Registered owners of a map's contents, ordered by <see cref="ISubsystem.Priority"/> —
    /// see <see cref="IMapScopedData"/>.</summary>
    public IEnumerable<IMapScopedData> MapScopedData => Facet<IMapScopedData>();

    /// <summary>Registered catalogs whose rows can be deleted along with the map that was their only
    /// user — see <see cref="IMapOwnableResourceFactory"/>.</summary>
    public IEnumerable<IMapOwnableResourceFactory> MapOwnableResourceFactories => Facet<IMapOwnableResourceFactory>();

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

    /// <summary>
    /// Opens one context and one transaction, runs <paramref name="body"/> against them, then commits —
    /// for a caller that needs several separate <see cref="EditorStorage"/> writes (an entity commit,
    /// image chunks, chunk changes, a tracking row) to land or fail together, instead of paying one
    /// transaction floor per call. The ADT importer's per-tile write is the motivating case.
    ///
    /// Inside <paramref name="body"/>, use only the <c>*WithinTransactionAsync</c> overloads (or
    /// <see cref="CommitEntitiesWithinTransactionAsync"/>) — the plain public methods each acquire the
    /// write lock and open their own context/transaction, which would deadlock or land outside this one.
    /// </summary>
    public async Task CommitTransactionAsync(Func<EditorDbContext, Task> body)
    {
        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();
        await context.Database.OpenConnectionAsync().ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync().ConfigureAwait(false);

        await body(context).ConfigureAwait(false);

        await transaction.CommitAsync().ConfigureAwait(false);
    }

    /// <summary>Stages and saves an entity commit against an already-open context — see
    /// <see cref="CommitTransactionAsync"/>. Multiple <c>SaveChangesAsync</c> calls against one context
    /// inside its manually-begun transaction all land in that one transaction; only its own commit
    /// finalizes them.</summary>
    public Task CommitEntitiesWithinTransactionAsync(EditorDbContext context, IReadOnlyList<IEntity> saves, IReadOnlyList<IEntity> deletes) =>
        StageAndSaveAsync(context, saves, deletes);

    // How many rows go into one INSERT … ON DUPLICATE KEY UPDATE. A whole-map commit stamps tens of
    // thousands of chunks; at three parameters a row this stays far under the wire-protocol limit.
    private const int ChunkChangeRowsPerStatement = 1000;

    /// <summary>
    /// Stamps every chunk a commit touched with the current time. One batched upsert per
    /// <see cref="ChunkChangeRowsPerStatement"/> rows, since a round-trip per chunk costs minutes of latency
    /// for a full-map edit.
    /// </summary>
    public async Task UpsertChunkChangesAsync(IReadOnlyCollection<(int Map, int X, int Y)> chunks)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();
        await context.Database.OpenConnectionAsync().ConfigureAwait(false);
        await UpsertChunkChangesCoreAsync(context, null, chunks).ConfigureAwait(false);
    }

    /// <summary>Runs within an already-open context and transaction — see <see cref="CommitTransactionAsync"/>.
    /// Does not take the write lock or open the connection itself.</summary>
    public Task UpsertChunkChangesWithinTransactionAsync(
        EditorDbContext context, DbTransaction transaction, IReadOnlyCollection<(int Map, int X, int Y)> chunks) =>
        chunks.Count == 0 ? Task.CompletedTask : UpsertChunkChangesCoreAsync(context, transaction, chunks);

    private async Task UpsertChunkChangesCoreAsync(
        EditorDbContext context, DbTransaction? transaction, IReadOnlyCollection<(int Map, int X, int Y)> chunks)
    {
        var clock = Stopwatch.StartNew();
        (string tableName, string mapColumn, string xColumn, string yColumn, string timeColumn) =
            ChunkChangeColumns(context);

        DbConnection connection = context.Database.GetDbConnection();

        DateTime now = DateTime.UtcNow;
        List<(int Map, int X, int Y)> all = chunks.ToList();

        for (int start = 0; start < all.Count; start += ChunkChangeRowsPerStatement)
        {
            int count = Math.Min(ChunkChangeRowsPerStatement, all.Count - start);
            await using DbCommand command = connection.CreateCommand();
            command.CommandTimeout = 60;
            command.Transaction = transaction;

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

    // How long a map-scoped bulk statement is allowed to run — a map can carry hundreds of thousands
    // of entities, and this covers both the delete and the read-only count run for the popup preview.
    private const int MapScopedCommandTimeoutSeconds = 300;

    /// <summary>Table and column name off the model for one property, so hand-written SQL can never
    /// silently desync from what EF maps a record to. Shared by every raw-SQL helper below, and by a
    /// <see cref="IMapOwnableResourceFactory"/>'s own batched id delete.</summary>
    internal static (string Table, string Column) ResolveColumn<TRecord>(EditorDbContext context, string propertyName)
        where TRecord : class
    {
        IEntityType type = context.Model.FindEntityType(typeof(TRecord))!;
        var table = StoreObjectIdentifier.Table(type.GetTableName()!, type.GetSchema());
        return (type.GetTableName()!, type.FindProperty(propertyName)!.GetColumnName(table)!);
    }

    /// <summary>The table and id/map column names of <see cref="MapEntityRecord"/> — what a
    /// component's map-scoped delete joins against to find the map's entity ids.</summary>
    private static (string Table, string Id, string Map) SceneEntityColumns(EditorDbContext context)
    {
        (string table, string mapColumn) = ResolveColumn<MapEntityRecord>(context, nameof(MapEntityRecord.MapId));
        (_, string idColumn) = ResolveColumn<MapEntityRecord>(context, nameof(MapEntityRecord.Id));
        return (table, idColumn, mapColumn);
    }

    // How many ids go into one batched delete statement's IN(...) list — see IMapOwnableResourceFactory.
    private const int DeleteByIdsBatchSize = 1000;

    /// <summary>
    /// Deletes every row of <typeparamref name="TRecord"/> whose <paramref name="idPropertyName"/> is
    /// one of <paramref name="ids"/>, batched at <see cref="DeleteByIdsBatchSize"/> ids per statement.
    /// What a <see cref="IMapOwnableResourceFactory"/> uses for its own id list and, for a resource with
    /// child rows (e.g. an image's chunks), for the child table too. Runs within an already-open
    /// context and transaction; see <see cref="CommitTransactionAsync"/>.
    /// </summary>
    public async Task DeleteByIdsAsync<TRecord>(
        EditorDbContext context, DbTransaction transaction, string idPropertyName, IReadOnlyCollection<int> ids)
        where TRecord : class
    {
        if (ids.Count == 0)
        {
            return;
        }

        (string table, string column) = ResolveColumn<TRecord>(context, idPropertyName);
        List<int> all = ids as List<int> ?? ids.ToList();

        for (int start = 0; start < all.Count; start += DeleteByIdsBatchSize)
        {
            int count = Math.Min(DeleteByIdsBatchSize, all.Count - start);
            await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
            command.CommandTimeout = MapScopedCommandTimeoutSeconds;
            command.Transaction = transaction;

            var sql = new StringBuilder($"DELETE FROM `{table}` WHERE `{column}` IN (");
            for (int i = 0; i < count; i++)
            {
                sql.Append(i == 0 ? "@i0" : $",@i{i}");
                AddParameter(command, $"@i{i}", all[start + i]);
            }

            sql.Append(')');
            command.CommandText = sql.ToString();
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deletes every row of <typeparamref name="TRecord"/> whose <paramref name="mapPropertyName"/>
    /// column equals <paramref name="map"/> — for a table keyed directly on the map, e.g. landscape
    /// settings or a landscape catalog table. Runs within an already-open context and transaction; see
    /// <see cref="CommitTransactionAsync"/>. See <see cref="IMapScopedData"/>.
    /// </summary>
    public async Task<int> DeleteWhereMapAsync<TRecord>(
        EditorDbContext context, DbTransaction transaction, string mapPropertyName, MapId map)
        where TRecord : class
    {
        (string table, string column) = ResolveColumn<TRecord>(context, mapPropertyName);
        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandTimeout = MapScopedCommandTimeoutSeconds;
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM `{table}` WHERE `{column}` = @m";
        AddParameter(command, "@m", map.Value);
        return await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>Read-only count of what <see cref="DeleteWhereMapAsync{TRecord}"/> would delete — for
    /// the delete popup's preview. <paramref name="context"/>'s connection does not need to be open
    /// yet; this opens it if needed.</summary>
    public async Task<int> CountWhereMapAsync<TRecord>(EditorDbContext context, string mapPropertyName, MapId map)
        where TRecord : class
    {
        (string table, string column) = ResolveColumn<TRecord>(context, mapPropertyName);
        if (context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync().ConfigureAwait(false);
        }

        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandTimeout = MapScopedCommandTimeoutSeconds;
        command.CommandText = $"SELECT COUNT(*) FROM `{table}` WHERE `{column}` = @m";
        AddParameter(command, "@m", map.Value);
        return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false));
    }

    /// <summary>
    /// Deletes every row of <typeparamref name="TRecord"/> whose <paramref name="entityIdPropertyName"/>
    /// names one of the map's scene entities — for a component table found only through its owning
    /// entity. Runs within an already-open context and transaction; see <see cref="CommitTransactionAsync"/>.
    /// See <see cref="IMapScopedData"/> and <see cref="ISceneComponentPersistence.DeleteForMapAsync"/>.
    /// </summary>
    public async Task<int> DeleteForMapEntitiesAsync<TRecord>(
        EditorDbContext context, DbTransaction transaction, string entityIdPropertyName, MapId map)
        where TRecord : class
    {
        (string table, string column) = ResolveColumn<TRecord>(context, entityIdPropertyName);
        (string entityTable, string entityIdColumn, string entityMapColumn) = SceneEntityColumns(context);

        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandTimeout = MapScopedCommandTimeoutSeconds;
        command.Transaction = transaction;
        command.CommandText =
            $"DELETE FROM `{table}` WHERE `{column}` IN (SELECT `{entityIdColumn}` FROM `{entityTable}` WHERE `{entityMapColumn}` = @m)";
        AddParameter(command, "@m", map.Value);
        return await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the <c>wms_entities</c> row of every editor-authored entity on <paramref name="map"/>.
    /// Foreign-key cascades take the <c>wms_map_entities</c> row and every tag and component row keyed on
    /// the identity. Runs within an already-open context and transaction; see <see cref="CommitTransactionAsync"/>.
    /// </summary>
    public async Task<int> DeleteMapEntityIdentitiesAsync(EditorDbContext context, DbTransaction transaction, MapId map)
    {
        (string identityTable, string identityId) = ResolveColumn<EntityRecord>(context, nameof(EntityRecord.Id));
        (string entityTable, string entityIdColumn, string entityMapColumn) = SceneEntityColumns(context);

        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandTimeout = MapScopedCommandTimeoutSeconds;
        command.Transaction = transaction;
        command.CommandText =
            $"DELETE FROM `{identityTable}` WHERE `{identityId}` IN (SELECT `{entityIdColumn}` FROM `{entityTable}` WHERE `{entityMapColumn}` = @m)";
        AddParameter(command, "@m", map.Value);
        return await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>Read-only count of what <see cref="DeleteForMapEntitiesAsync{TRecord}"/> would delete.
    /// <paramref name="context"/>'s connection does not need to be open yet; this opens it if needed.</summary>
    public async Task<int> CountForMapEntitiesAsync<TRecord>(EditorDbContext context, string entityIdPropertyName, MapId map)
        where TRecord : class
    {
        (string table, string column) = ResolveColumn<TRecord>(context, entityIdPropertyName);
        (string entityTable, string entityIdColumn, string entityMapColumn) = SceneEntityColumns(context);

        if (context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync().ConfigureAwait(false);
        }

        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandTimeout = MapScopedCommandTimeoutSeconds;
        command.CommandText =
            $"SELECT COUNT(*) FROM `{table}` WHERE `{column}` IN (SELECT `{entityIdColumn}` FROM `{entityTable}` WHERE `{entityMapColumn}` = @m)";
        AddParameter(command, "@m", map.Value);
        return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false));
    }

    /// <summary>
    /// The resources each registered <see cref="IMapOwnableResourceFactory"/> owns that are referenced
    /// only by <paramref name="map"/> — no other map, including the prefab library (-1) and an id with
    /// no <c>wms_maps</c> row. A resource kind with no <see cref="IResourceReferencingPersistence"/> for
    /// its type is left out entirely: nothing can prove it isn't used elsewhere. Must run before the
    /// map's entities are deleted — see <see cref="IMapScopedData"/>.
    /// </summary>
    public async Task<IReadOnlyDictionary<Type, IReadOnlyList<int>>> FindMapOnlyResourcesAsync(EditorDbContext context, MapId map)
    {
        var result = new Dictionary<Type, IReadOnlyList<int>>();

        foreach (IMapOwnableResourceFactory resourceFactory in MapOwnableResourceFactories)
        {
            IResourceReferencingPersistence? persistence = ComponentPersistence
                .OfType<IResourceReferencingPersistence>()
                .FirstOrDefault(candidate => candidate.ReferencedResourceType == resourceFactory.ResourceType);
            if (persistence == null)
            {
                continue;
            }

            IReadOnlyList<(int ResourceId, MapId Map)> references = await persistence.ReferencesAsync(context).ConfigureAwait(false);
            ILookup<int, MapId> byResource = references.ToLookup(reference => reference.ResourceId, reference => reference.Map);

            List<int> mapOnly = byResource
                .Where(group => group.Contains(map) && group.All(referencingMap => referencingMap == map))
                .Select(group => group.Key)
                .Where(id => !IsResourceLive(resourceFactory.ResourceType, id))
                .ToList();

            result[resourceFactory.ResourceType] = mapOnly;
        }

        return result;
    }

    /// <summary>Whether a resource id is pinned by the active edit session, or loaded but not yet
    /// saved — belt-and-braces guards against purging a resource the (already-clean) session still has
    /// unsaved state for. See <see cref="IMapScopedData"/>'s guard.</summary>
    private bool IsResourceLive(Type resourceType, int recordId)
    {
        foreach (CatalogEntity entity in Context.Catalog.Entities)
        {
            if (!resourceType.IsInstanceOfType(entity)
                || entity is not IKeyedCatalogEntity keyed
                || keyed.RecordId != recordId)
            {
                continue;
            }

            if (Context.Database.IsPinned(entity) || !keyed.IsSaved)
            {
                return true;
            }
        }

        return false;
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
        List<(PaintImage Image, IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)> Chunks)> databaseWrites =
            await WriteDiskImagesAndSplitAsync(writes).ConfigureAwait(false);
        if (databaseWrites.Count == 0)
        {
            return;
        }

        var clock = Stopwatch.StartNew();
        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();
        await context.Database.OpenConnectionAsync().ConfigureAwait(false);

        int totalChunks = await UpsertImageChunksCoreAsync(context, null, databaseWrites).ConfigureAwait(false);
        GD.Print($"[ImageChunks] Upserted {totalChunks} chunk(s) across {databaseWrites.Count} image(s) in {clock.ElapsedMilliseconds}ms.");
    }

    /// <summary>Runs within an already-open context and transaction — see <see cref="CommitTransactionAsync"/>.
    /// Does not take the write lock or open the connection itself.</summary>
    public async Task UpsertImageChunksWithinTransactionAsync(
        EditorDbContext context, DbTransaction transaction,
        IReadOnlyDictionary<PaintImage, IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)>> writes)
    {
        List<(PaintImage Image, IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)> Chunks)> databaseWrites =
            await WriteDiskImagesAndSplitAsync(writes).ConfigureAwait(false);
        if (databaseWrites.Count == 0)
        {
            return;
        }

        var clock = Stopwatch.StartNew();
        int totalChunks = await UpsertImageChunksCoreAsync(context, transaction, databaseWrites).ConfigureAwait(false);
        GD.Print($"[ImageChunks] Upserted {totalChunks} chunk(s) across {databaseWrites.Count} image(s) in {clock.ElapsedMilliseconds}ms.");
    }

    /// <summary>Writes every disk-backed image's chunks (not transactional with the database — see the
    /// disk-backed-PaintImages note) and returns only the database-backed writes left to upsert.</summary>
    private static async Task<List<(PaintImage Image, IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)> Chunks)>> WriteDiskImagesAndSplitAsync(
        IReadOnlyDictionary<PaintImage, IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)>> writes)
    {
        foreach ((PaintImage image, IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)> chunks) in writes)
        {
            if (chunks.Count > 0 && image.IsDiskBacked)
            {
                await new ImageDiskStore().WriteChunksAsync(image, chunks, Array.Empty<ImageChunkCoord>()).ConfigureAwait(false);
            }
        }

        return writes
            .Where(pair => pair.Value.Count > 0 && !pair.Key.IsDiskBacked)
            .Select(pair => (pair.Key, pair.Value))
            .ToList();
    }

    private static async Task<int> UpsertImageChunksCoreAsync(
        EditorDbContext context,
        DbTransaction? transaction,
        List<(PaintImage Image, IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)> Chunks)> databaseWrites)
    {
        (string table, string idColumn, string xColumn, string yColumn, string formatColumn, string pixelsColumn) =
            ImageChunkColumns(context);

        DbConnection connection = context.Database.GetDbConnection();

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
                command.Transaction = transaction;

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

        return totalChunks;
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
