using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using WorldMapStudio.Cata;

namespace WorldMapStudio;

/// <summary>
/// A named data backend: a dolt repository reached through short-lived EF Core connections. Concrete
/// storages self-register with [Subsystem(nameof(DatabaseSystem))] and host their entity factories
/// (which name the concrete storage type). Dolt speaks the MySQL 8 wire protocol, so contexts are
/// built with the Pomelo MySQL provider against <see cref="Connection"/>.
/// </summary>
public abstract class Storage : ISubsystem
{
    public abstract string Name { get; }

    public virtual float Priority => 0f;

    /// <summary>How this storage reaches its database. Bound from project settings at startup.</summary>
    public virtual StorageConnection Connection { get; private set; } = new();

    /// <summary>
    /// Whether this storage's <see cref="Connection"/> is its own, persisted per-project setting.
    /// A storage that shares another's connection instead (overriding <see cref="Connection"/> to
    /// proxy it, so both point at one physical database) returns false, so
    /// <see cref="DatabaseSystem.BindConnections"/> does not seed a redundant, unused project entry
    /// for it and <see cref="DatabaseSystem.Startup"/> does not try to launch a second server for the
    /// same repository.
    /// </summary>
    public virtual bool OwnsConnection => true;

    /// <summary>Guards this storage's database: concurrent scans (readers), exclusive commit (writer).</summary>
    public AsyncReaderWriterLock Lock { get; } = new();

    /// <summary>The connection a brand-new project gets for this storage, before the user edits it.</summary>
    public virtual StorageConnection CreateDefaultConnection() => new();

    internal void BindConnection(StorageConnection connection) => Connection = connection;

    /// <summary>
    /// This storage's own subsystem tree — its self-registered factories, sources, browsers, etc. Each
    /// concrete storage implements this as a one-line forward to its own generated <c>Subsystems</c>
    /// (from <c>[Subsystem(nameof(ConcreteStorage))]</c>), so the facets below only have to be written
    /// once instead of every concrete storage re-declaring the same <c>Subsystems.OfType&lt;X&gt;()</c>
    /// boilerplate. Abstract rather than routed through <see cref="ISubsystemHost"/>'s default
    /// implementation on purpose: a storage that forgets to implement this fails to compile instead of
    /// silently reporting every facet as empty.
    /// </summary>
    protected abstract IEnumerable<ISubsystem> HostedSubsystems { get; }

    // Subsystems are constructed once at startup and never change afterward — the same assumption
    // ISceneComponentPersistence's doc relies on for EF's model cache — so each facet below only ever
    // needs to filter HostedSubsystems once, the first time it's read, rather than on every access.
    // Storage.EntityFactories is enumerated once per entity inside the Persist/CommitAsync loop, so an
    // uncached OfType<> here was rescanning every subsystem in the whole editor per entity committed.
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

    /// <summary>Scene-entity factories creatable by script and UI through one shared method — see
    /// <see cref="ISpawnFactory"/>. Storage-agnostic, like <see cref="CatalogBrowsers"/>.</summary>
    public virtual IEnumerable<ISpawnFactory> Spawners => Facet<ISpawnFactory>();

    /// <summary>One-time seed scripts registered into this storage — see <see cref="ISeedSql"/>.</summary>
    public virtual IEnumerable<ISeedSql> Seeds => Facet<ISeedSql>();

    /// <summary>Table name the seed-history tracking table gets in any storage database — excluded
    /// from <see cref="MigrationSystem"/>'s drop-table proposals, since no EF model ever declares it.</summary>
    public const string SeedHistoryTableName = "wms_seed_history";

    /// <summary>Creates the storage's tables when the database is empty. Drift is handled by migrations.</summary>
    public virtual void EnsureSchema() { }

    /// <summary>The schema this storage's EF model expects, or null if it has no context to compare.</summary>
    public virtual Schema? ExpectedSchema() => null;

    /// <summary>Reads the storage database's actual schema.</summary>
    public Task<Schema> ReadLiveSchemaAsync() =>
        LiveSchema.ReadAsync(Connection.BuildConnectionString(), Connection.Database);

    /// <summary>Runs the given migration SQL (see <see cref="SqlScript.SplitStatements"/>) under the write lock.</summary>
    public async Task ApplySqlAsync(string sql)
    {
        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using var connection = new MySqlConnection(Connection.BuildConnectionString());
        await connection.OpenAsync().ConfigureAwait(false);
        await RunStatementsAsync(connection, sql).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs every registered <see cref="ISeedSql"/> not already recorded in this storage database's
    /// seed-history table, under the write lock — each seed's statements, then a row recording it
    /// done, so a seed that fails partway through is retried whole next startup rather than left
    /// half-applied and marked complete.
    /// </summary>
    public async Task ApplySeedsAsync()
    {
        List<ISeedSql> seeds = Seeds.ToList();
        if (seeds.Count == 0)
        {
            return;
        }

        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using var connection = new MySqlConnection(Connection.BuildConnectionString());
        await connection.OpenAsync().ConfigureAwait(false);

        await using (MySqlCommand create = connection.CreateCommand())
        {
            create.CommandText =
                $"CREATE TABLE IF NOT EXISTS `{SeedHistoryTableName}` (`name` VARCHAR(255) NOT NULL PRIMARY KEY, `applied_at` DATETIME NOT NULL);";
            await create.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var applied = new HashSet<string>(StringComparer.Ordinal);
        await using (MySqlCommand select = connection.CreateCommand())
        {
            select.CommandText = $"SELECT `name` FROM `{SeedHistoryTableName}`;";
            await using MySqlDataReader reader = await select.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                applied.Add(reader.GetString(0));
            }
        }

        foreach (ISeedSql seed in seeds.Where(seed => !applied.Contains(seed.Name)))
        {
            await RunStatementsAsync(connection, seed.Sql).ConfigureAwait(false);

            await using MySqlCommand record = connection.CreateCommand();
            record.CommandText = $"INSERT INTO `{SeedHistoryTableName}` (`name`, `applied_at`) VALUES (@name, UTC_TIMESTAMP());";
            record.Parameters.AddWithValue("@name", seed.Name);
            await record.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private static async Task RunStatementsAsync(MySqlConnection connection, string sql)
    {
        foreach (string statement in SqlScript.SplitStatements(sql))
        {
            await using MySqlCommand command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
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
    /// queued. <see cref="EditorStorage"/> and <see cref="CataStorage"/> differ only in which concrete
    /// <see cref="DbContext"/> type they open.
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

    /// <summary>Builds Pomelo MySQL options for one of this storage's contexts. The diagnostic
    /// interceptors are always registered and gate themselves on <see cref="DiagnosticLog.Enabled"/>:
    /// registering them conditionally would change the options shape mid-session and rebuild EF's
    /// cached internal service provider on the next context.</summary>
    protected DbContextOptions<TContext> BuildOptions<TContext>() where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseMySql(Connection.BuildConnectionString(), new MySqlServerVersion(new Version(8, 0, 0)))
            .AddInterceptors(SqlCommandLog.Instance, SqlConnectionLog.Instance)
            .Options;
}
