using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

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

    /// <summary>The scene-entity factories registered into this storage.</summary>
    public virtual IEnumerable<ISceneEntityFactory> SceneFactories => Enumerable.Empty<ISceneEntityFactory>();

    /// <summary>The catalog-entity factories registered into this storage.</summary>
    public virtual IEnumerable<ICatalogEntityFactory> CatalogFactories => Enumerable.Empty<ICatalogEntityFactory>();

    /// <summary>The lazily-loaded catalog-entity factories registered into this storage — see
    /// <see cref="ILazyCatalogEntityFactory"/>.</summary>
    public virtual IEnumerable<ILazyCatalogEntityFactory> LazyCatalogFactories => Enumerable.Empty<ILazyCatalogEntityFactory>();

    /// <summary>Every factory in this storage, whatever kind of entity it persists.</summary>
    public IEnumerable<IEntityFactory> EntityFactories =>
        SceneFactories.Cast<IEntityFactory>().Concat(CatalogFactories).Concat(LazyCatalogFactories);

    /// <summary>The map sources registered into this storage; empty if it holds no maps.</summary>
    public virtual IEnumerable<IMapSource> MapSources => Enumerable.Empty<IMapSource>();

    /// <summary>The landscape settings sources registered into this storage.</summary>
    public virtual IEnumerable<ILandscapeSettingsSource> LandscapeSettingsSources => Enumerable.Empty<ILandscapeSettingsSource>();

    /// <summary>Creates the storage's tables when the database is empty. Drift is handled by migrations.</summary>
    public virtual void EnsureSchema() { }

    /// <summary>The schema this storage's EF model expects, or null if it has no context to compare.</summary>
    public virtual Schema? ExpectedSchema() => null;

    /// <summary>Reads the storage database's actual schema.</summary>
    public Task<Schema> ReadLiveSchemaAsync() =>
        LiveSchema.ReadAsync(Connection.BuildConnectionString(), Connection.Database);

    /// <summary>Runs the given migration SQL (statements split on ';') under the write lock.</summary>
    public async Task ApplySqlAsync(string sql)
    {
        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using var connection = new MySqlConnection(Connection.BuildConnectionString());
        await connection.OpenAsync().ConfigureAwait(false);

        foreach (string statement in sql.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0))
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

    protected IEntityFactory? FactoryFor(IEntity entity) =>
        EntityFactories.FirstOrDefault(factory => factory.Handles(entity));

    /// <summary>Builds Pomelo MySQL options for one of this storage's contexts.</summary>
    protected DbContextOptions<TContext> BuildOptions<TContext>() where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseMySql(Connection.BuildConnectionString(), new MySqlServerVersion(new Version(8, 0, 0)))
            .Options;
}
