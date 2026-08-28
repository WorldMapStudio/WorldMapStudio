using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MySqlConnector;

namespace WorldMapStudio;

/// <summary>
/// Hosts the editor's data backends. Storages self-register with [Subsystem(nameof(DatabaseSystem))]
/// and are constructed by the generated InitializeSubsystems(), so the built-in "Editor" storage and
/// any plugin storages register without touching this class. A plain member of
/// <see cref="EditorContext"/> (the core spine, not an extension point), but itself a host.
/// On <see cref="Startup"/> it launches a managed <c>dolt sql-server</c> for each storage configured
/// to launch one, then ensures each storage's database exists.
/// </summary>
public sealed partial class DatabaseSystem : ISubsystemHost, IEditSessionStore
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(15);

    private readonly EditorContext _context;
    private readonly List<DoltServer> _servers = [];

    public IEnumerable<Storage> Storages => Subsystems.Cast<Storage>();

    public DatabaseSystem(EditorContext context)
    {
        _context = context;
        InitializeSubsystems();
        BindConnections();
    }

    public EditorContext Context => _context;

    // Each storage reads its connection from the project's settings, which are seeded with the
    // storage's defaults the first time a project uses it.
    private void BindConnections()
    {
        foreach (Storage storage in Storages)
        {
            StorageConnection connection = _context.Project.GetOrAddStorageConnection(storage.Name, storage.CreateDefaultConnection());
            storage.BindConnection(connection);
        }
    }

    /// <summary>Launches managed dolt servers and ensures each storage's database exists.</summary>
    public void Startup()
    {
        foreach (Storage storage in Storages)
        {
            StorageConnection connection = storage.Connection;
            if (connection.LaunchServer)
            {
                if (connection.RepositoryPath.Length == 0)
                {
                    connection.RepositoryPath = DefaultDataDirectory();
                }

                var server = new DoltServer(connection.RepositoryPath, connection.Host, connection.Port);
                if (!server.Start(StartTimeout))
                {
                    GD.PushError($"[Database] Storage '{storage.Name}' server failed to start.");
                    continue;
                }

                _servers.Add(server);
            }

            EnsureDatabase(storage);

            try
            {
                storage.EnsureSchema();
            }
            catch (Exception e)
            {
                GD.PushError($"[Database] Storage '{storage.Name}' schema check failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Loads a catalog whole into <see cref="EditorContext.Catalog"/>, replacing anything of that type
    /// already loaded, and returns it. Catalog lifetime belongs to whichever system owns the catalog —
    /// nothing streams these — so it calls this when it needs the set and
    /// <see cref="UnloadCatalog{TEntity}"/> when it is done.
    /// </summary>
    public IReadOnlyList<TEntity> LoadCatalog<TEntity>() where TEntity : CatalogEntity
    {
        _context.Catalog.RemoveAll<TEntity>();

        var loaded = new List<TEntity>();
        foreach (Storage storage in Storages)
        {
            foreach (ICatalogEntityFactory factory in storage.CatalogFactories)
            {
                if (!typeof(TEntity).IsAssignableFrom(factory.EntityType))
                {
                    continue;
                }

                try
                {
                    foreach (CatalogEntity entity in Read(storage, factory.LoadAllAsync))
                    {
                        if (entity is TEntity typed)
                        {
                            _context.Catalog.Add(typed);
                            loaded.Add(typed);
                        }
                    }
                }
                catch (Exception e)
                {
                    GD.PushError($"[Database] Loading catalog {typeof(TEntity).Name} from '{storage.Name}' failed: {e.Message}");
                }
            }
        }

        return loaded;
    }

    /// <summary>Drops a loaded catalog. Entities the edit session pinned stay alive until it ends.</summary>
    public void UnloadCatalog<TEntity>() where TEntity : CatalogEntity => _context.Catalog.RemoveAll<TEntity>();

    /// <summary>
    /// Persists everything the session touched, grouped per storage into one transaction each — scene
    /// and catalog entities together, so a session that edited both commits atomically. A pinned entity
    /// still registered (in the scene or the catalog) is saved; one that has left (an undone creation or
    /// a deletion) is deleted.
    ///
    /// Reached through <see cref="IEditSessionStore"/> from <see cref="EditSessionManager.Commit"/>,
    /// never called directly — committing is one act, not a write followed by a clear the caller has to
    /// remember.
    /// </summary>
    public void Persist(EditSession session)
    {
        var committed = new HashSet<IEntity>();

        foreach (Storage storage in Storages)
        {
            var saves = new List<IEntity>();
            var deletes = new List<IEntity>();

            foreach (IEntity entity in session.Pinned)
            {
                // Derived entities are computed, never stored. The session already refuses to pin
                // one, so this is the second lock on the door that actually matters: whatever else
                // goes wrong, a computed result must not reach the database.
                if (entity is IDerivedEntity)
                {
                    continue;
                }

                if (storage.EntityFactories.Any(factory => factory.Handles(entity)))
                {
                    (IsLoaded(entity) ? saves : deletes).Add(entity);
                }
            }

            if (saves.Count == 0 && deletes.Count == 0)
            {
                continue;
            }

            try
            {
                BlockingWork.Run(() => storage.CommitAsync(saves, deletes));
                foreach (IEntity entity in saves)
                {
                    committed.Add(entity);
                }

                foreach (IEntity entity in deletes)
                {
                    committed.Add(entity);
                }
            }
            catch (Exception e)
            {
                GD.PushError($"[Database] Commit failed for '{storage.Name}': {e.Message}");
            }
        }

        if (committed.Count > 0)
        {
            try
            {
                _context.Exports.Changes.RecordCommit(session, committed.Contains);
            }
            catch (Exception e)
            {
                GD.PushError($"[Export] Recording chunk changes failed: {e.Message}");
            }
        }
    }

    public void Shutdown()
    {
        foreach (DoltServer server in _servers)
        {
            server.Stop();
        }

        _servers.Clear();
    }

    // Whether the entity is still loaded, which is what separates a save from a delete. System
    // entities are always loaded, so they are always a save.
    private bool IsLoaded(IEntity entity) => entity switch
    {
        SceneEntity scene => _context.Scene.Contains(scene),
        CatalogEntity catalog => _context.Catalog.Contains(catalog),
        _ => true,
    };

    // Reads under the storage's reader lock. Blocks the caller — see <see cref="BlockingWork"/>.
    private static IReadOnlyList<T> Read<T>(Storage storage, Func<Task<IReadOnlyList<T>>> read) =>
        BlockingWork.Run(async () =>
        {
            using IDisposable reader = await storage.Lock.ReaderAsync().ConfigureAwait(false);
            return await read().ConfigureAwait(false);
        });

    private static void EnsureDatabase(Storage storage)
    {
        StorageConnection connection = storage.Connection;
        if (connection.Database.Length == 0)
        {
            return;
        }

        try
        {
            using var conn = new MySqlConnection(connection.BuildConnectionString(includeDatabase: false));
            conn.Open();
            using MySqlCommand cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE IF NOT EXISTS `{connection.Database}`;";
            cmd.ExecuteNonQuery();
            GD.Print($"[Database] Storage '{storage.Name}' ready ({connection.Host}:{connection.Port}/{connection.Database}).");
        }
        catch (Exception e)
        {
            GD.PushError($"[Database] Storage '{storage.Name}' database check failed: {e.Message}");
        }
    }

    private string DefaultDataDirectory() =>
        Path.Combine(ProjectStore.ProjectFolder(_context.Project.Name), "dolt");
}
