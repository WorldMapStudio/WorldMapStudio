using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>
/// A named data backend: a DoltLite file reached through short-lived EF Core connections. Concrete
/// storages self-register with [Subsystem(nameof(DatabaseSystem))] and host their entity factories
/// (which name the concrete storage type). DoltLite speaks the SQLite dialect, so contexts are built
/// with the EF Core Sqlite provider against <see cref="Location"/>.
/// </summary>
public abstract class Storage : ISubsystem
{
    public abstract string Name { get; }

    public virtual float Priority => 0f;

    /// <summary>Where this storage's data lives. Bound from project settings at startup.</summary>
    public virtual StorageLocation Location { get; private set; } = new();

    /// <summary>
    /// Whether this storage's <see cref="Location"/> is its own, persisted per-project setting.
    /// A storage that shares another's location instead (overriding <see cref="Location"/> to
    /// proxy it, so both point at one physical file) returns false, so
    /// <see cref="DatabaseSystem.BindLocations"/> does not seed a redundant, unused project entry
    /// for it.
    /// </summary>
    public virtual bool OwnsLocation => true;

    /// <summary>Guards this storage's database: concurrent scans (readers), exclusive commit (writer).</summary>
    public AsyncReaderWriterLock Lock { get; } = new();

    /// <summary>The location a brand-new project gets for this storage, before the user edits it.</summary>
    public virtual StorageLocation CreateDefaultLocation() => new();

    internal void BindLocation(StorageLocation location) => Location = location;

    /// <summary>This storage's own subsystem tree: the <see cref="ISubsystemHost.Subsystems"/> of a concrete storage that hosts subsystems.</summary>
    protected virtual IEnumerable<ISubsystem> HostedSubsystems => this is ISubsystemHost host ? host.Subsystems : [];

    // Subsystems never change after startup, so each facet filters HostedSubsystems once and caches the
    // result. EntityFactories is enumerated once per entity in the commit loop.
    private readonly Dictionary<Type, object> _facetCache = new();

    /// <summary>Caches the subsystems of type <typeparamref name="T"/> from <see cref="HostedSubsystems"/>
    /// on first access, for a concrete storage's own extra facets (e.g. <c>EditorStorage.ComponentPersistence</c>)
    /// that don't otherwise go through one of the facets already declared here.</summary>
    protected IReadOnlyList<T> Facet<T>()
    {
        if (_facetCache.TryGetValue(typeof(T), out object? cached))
        {
            return (IReadOnlyList<T>)cached;
        }

        List<T> list = HostedSubsystems.OfType<T>().ToList();
        _facetCache[typeof(T)] = list;
        return list;
    }

    /// <summary>The scene-entity factories registered into this storage. A storage that needs entries
    /// from somewhere other than its own subsystem tree can still override this.</summary>
    public virtual IEnumerable<ISceneEntityFactory> SceneFactories => Facet<ISceneEntityFactory>();

    /// <summary>The catalog-entity factories registered into this storage.</summary>
    public virtual IEnumerable<ICatalogEntityFactory> CatalogFactories => Facet<ICatalogEntityFactory>();

    /// <summary>The lazily-loaded catalog-entity factories registered into this storage — see
    /// <see cref="ILazyCatalogEntityFactory"/>.</summary>
    public virtual IEnumerable<ILazyCatalogEntityFactory> LazyCatalogFactories => Facet<ILazyCatalogEntityFactory>();

    private IReadOnlyList<IEntityFactory>? _entityFactories;

    /// <summary>Every factory in this storage, whatever kind of entity it persists.</summary>
    public IEnumerable<IEntityFactory> EntityFactories => _entityFactories ??=
        SceneFactories.Cast<IEntityFactory>().Concat(CatalogFactories).Concat(LazyCatalogFactories).ToList();

    /// <summary>The map sources registered into this storage; empty if it holds no maps.</summary>
    public virtual IEnumerable<IMapSource> MapSources => Facet<IMapSource>();

    /// <summary>The landscape settings sources registered into this storage.</summary>
    public virtual IEnumerable<ILandscapeSettingsSource> LandscapeSettingsSources => Facet<ILandscapeSettingsSource>();

    /// <summary>The table configurations registered into this storage — see
    /// <see cref="ITableConfiguration"/>. Gathered by <c>CreateContext</c> and passed to the storage's
    /// <c>DbContext</c>, so its <c>OnModelCreating</c> never has to name a table's owner by hand.</summary>
    public virtual IEnumerable<ITableConfiguration> TableConfigurations => Facet<ITableConfiguration>();

    /// <summary>Catalogs browsable in a catalog browser window — see <see cref="ICatalogBrowser"/>.
    /// Storage-agnostic, so consumers do <c>Storages.SelectMany(s => s.CatalogBrowsers)</c> instead of
    /// naming a specific storage.</summary>
    public virtual IEnumerable<ICatalogBrowser> CatalogBrowsers => Facet<ICatalogBrowser>();

    /// <summary>Extra ways to search a catalog's results — see <see cref="ICatalogSearchView"/>.
    /// Storage-agnostic like <see cref="CatalogBrowsers"/>: a view can serve catalogs hosted by any
    /// storage, not just its own.</summary>
    public virtual IEnumerable<ICatalogSearchView> CatalogSearchViews => Facet<ICatalogSearchView>();

    /// <summary>Scene-entity factories creatable by script and UI through one shared method — see
    /// <see cref="ISpawnFactory"/>. Storage-agnostic, like <see cref="CatalogBrowsers"/>.</summary>
    public virtual IEnumerable<ISpawnFactory> Spawners => Facet<ISpawnFactory>();

    /// <summary>One-time seed scripts registered into this storage — see <see cref="ISeedSql"/>.</summary>
    public virtual IEnumerable<ISeedSql> Seeds => Facet<ISeedSql>();

    /// <summary>Table name the seed-history tracking table gets in any storage database — excluded
    /// from <see cref="MigrationSystem"/>'s drop-table proposals, since no EF model ever declares it.</summary>
    public const string SeedHistoryTableName = "wms_seed_history";

    /// <summary>Whether this storage creates and migrates <paramref name="table"/>. A table it does not
    /// own is only validated against <see cref="ExpectedSchema"/> and never created, altered or dropped.</summary>
    public virtual bool OwnsTable(string table) => true;

    /// <summary>Creates the storage's tables when the database is empty. Drift is handled by migrations.</summary>
    public virtual void EnsureSchema() { }

    /// <summary>The schema this storage's EF model expects, or null if it has no context to compare.</summary>
    public virtual Schema? ExpectedSchema() => null;

    /// <summary>Reads the storage database's actual schema.</summary>
    public Task<Schema> ReadLiveSchemaAsync() => LiveSchema.ReadAsync(Location.BuildConnectionString());

    /// <summary>Opens a new, unopened connection to this storage's database. Raw-SQL callers use this
    /// instead of constructing <see cref="SqliteConnection"/> directly.</summary>
    protected DbConnection OpenConnection() => new SqliteConnection(Location.BuildConnectionString());

    /// <summary>Runs the given migration SQL (see <see cref="SqlScript.SplitStatements"/>) under the write
    /// lock, in one transaction — SQLite DDL is transactional, so a failed migration never leaves the
    /// database half-migrated.</summary>
    public async Task ApplySqlAsync(string sql)
    {
        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using DbConnection connection = OpenConnection();
        await connection.OpenAsync().ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await RunStatementsAsync(connection, transaction, sql).ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Runs every registered <see cref="ISeedSql"/> not already recorded in this storage database's
    /// seed-history table, under the write lock — each seed's statements plus a row recording it done,
    /// in one transaction, so a seed that fails partway through is retried whole next startup rather
    /// than left half-applied and marked complete.
    /// </summary>
    public async Task ApplySeedsAsync()
    {
        List<ISeedSql> seeds = Seeds.ToList();
        if (seeds.Count == 0)
        {
            return;
        }

        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using DbConnection connection = OpenConnection();
        await connection.OpenAsync().ConfigureAwait(false);

        await using (DbCommand create = connection.CreateCommand())
        {
            create.CommandText =
                $"CREATE TABLE IF NOT EXISTS \"{SeedHistoryTableName}\" (\"name\" TEXT NOT NULL PRIMARY KEY, \"applied_at\" TEXT NOT NULL);";
            await create.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var applied = new HashSet<string>(StringComparer.Ordinal);
        await using (DbCommand select = connection.CreateCommand())
        {
            select.CommandText = $"SELECT \"name\" FROM \"{SeedHistoryTableName}\";";
            await using DbDataReader reader = await select.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                applied.Add(reader.GetString(0));
            }
        }

        foreach (ISeedSql seed in seeds.Where(seed => !applied.Contains(seed.Name)))
        {
            await using DbTransaction transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
            await RunStatementsAsync(connection, transaction, seed.Sql).ConfigureAwait(false);

            await using (DbCommand record = connection.CreateCommand())
            {
                record.Transaction = transaction;
                record.CommandText = $"INSERT INTO \"{SeedHistoryTableName}\" (\"name\", \"applied_at\") VALUES (@name, @appliedAt);";
                AddParameter(record, "@name", seed.Name);
                AddParameter(record, "@appliedAt", DateTime.UtcNow);
                await record.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await transaction.CommitAsync().ConfigureAwait(false);
        }
    }

    private static async Task RunStatementsAsync(DbConnection connection, DbTransaction transaction, string sql)
    {
        foreach (string statement in SqlScript.SplitStatements(sql))
        {
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// Persists the given saves and deletes in a single transaction against this storage. Entities of
    /// any kind may be mixed: a session that edited a material and moved a building commits both or
    /// neither.
    /// </summary>
    public virtual Task CommitAsync(IReadOnlyList<IEntity> saves, IReadOnlyList<IEntity> deletes) => Task.CompletedTask;

    /// <summary>
    /// Shared body for a concrete storage's <see cref="CommitAsync(IReadOnlyList{IEntity}, IReadOnlyList{IEntity})"/>
    /// override: opens a context via <paramref name="createContext"/> under the write lock, stages every
    /// save/delete through <see cref="FactoryFor"/>, saves once, then runs the write-backs the factories
    /// queued. Concrete storages differ only in which concrete <see cref="DbContext"/> type they open.
    /// </summary>
    protected async Task CommitAsync(Func<DbContext> createContext, IReadOnlyList<IEntity> saves, IReadOnlyList<IEntity> deletes)
    {
        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using DbContext context = createContext();
        await StageAndSaveAsync(context, saves, deletes).ConfigureAwait(false);
    }

    /// <summary>
    /// The staging + save half of <see cref="CommitAsync(Func{DbContext}, IReadOnlyList{IEntity}, IReadOnlyList{IEntity})"/>,
    /// without acquiring the write lock or managing the context's lifetime — for a caller that already
    /// holds both because it is folding this commit into a larger shared transaction alongside other
    /// writes (see <see cref="EditorStorage.CommitTransactionAsync"/>). A plain <see cref="CommitAsync(IReadOnlyList{IEntity}, IReadOnlyList{IEntity})"/>
    /// call is still the right choice when this is the only write in the transaction.
    /// </summary>
    protected async Task StageAndSaveAsync(DbContext context, IReadOnlyList<IEntity> saves, IReadOnlyList<IEntity> deletes)
    {
        foreach (IEntityFactory factory in saves.Select(FactoryFor).OfType<IEntityFactory>().Distinct())
        {
            await factory.PrepareBatchAsync(context, saves).ConfigureAwait(false);
        }

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

        // A single SaveChanges wraps all staged inserts/updates/deletes in one transaction — the
        // context's ambient one when the caller already began one, an implicit one otherwise.
        await context.SaveChangesAsync().ConfigureAwait(false);

        foreach (Action writeBack in writeBacks)
        {
            writeBack();
        }
    }

    protected IEntityFactory? FactoryFor(IEntity entity) =>
        EntityFactories.FirstOrDefault(factory => factory.Handles(entity));

    /// <summary>Builds EF Core Sqlite options for one of this storage's contexts. The diagnostic
    /// interceptors are always registered and gate themselves on <see cref="DiagnosticLog.Enabled"/>:
    /// registering them conditionally would change the options shape mid-session and rebuild EF's
    /// cached internal service provider on the next context.</summary>
    protected DbContextOptions<TContext> BuildOptions<TContext>() where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseSqlite(Location.BuildConnectionString())
            .AddInterceptors(SqlCommandLog.Instance, SqlConnectionLog.Instance)
            .Options;
}
