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
public sealed partial class DatabaseSystem : ISubsystemHost, IEditSessionStore, IWorldParticipant
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
            if (!storage.OwnsConnection)
            {
                continue;
            }

            StorageConnection connection = _context.Project.GetOrAddStorageConnection(storage.Name, storage.CreateDefaultConnection());
            storage.BindConnection(connection);
        }
    }

    /// <summary>
    /// Launches managed dolt servers and ensures each storage's database exists.
    /// <paramref name="confirmKillStray"/> is forwarded to <see cref="DoltServer.Start"/> so a caller on
    /// the main thread can prompt the user before killing a leftover server from a previous session.
    /// </summary>
    public void Startup(Func<string, bool>? confirmKillStray = null)
    {
        foreach (Storage storage in Storages)
        {
            // A storage that shares another's connection (see Storage.OwnsConnection) neither launches
            // its own server nor creates its own database — the owning storage already did both — but
            // it still gets its own EnsureSchema() call, since it owns a disjoint set of tables within
            // that shared database.
            if (storage.OwnsConnection)
            {
                StorageConnection connection = storage.Connection;
                if (connection.LaunchServer)
                {
                    if (connection.RepositoryPath.Length == 0)
                    {
                        connection.RepositoryPath = DefaultDataDirectory();
                    }

                    var server = new DoltServer(connection.RepositoryPath, connection.Host, connection.Port);
                    if (!server.Start(StartTimeout, confirmKillStray))
                    {
                        GD.PushError($"[Database] Storage '{storage.Name}' server failed to start.");
                        continue;
                    }

                    _servers.Add(server);
                }

                EnsureDatabase(storage);
            }

            try
            {
                storage.EnsureSchema();
            }
            catch (Exception e)
            {
                GD.PushError($"[Database] Storage '{storage.Name}' schema check failed: {e.Message}");
                continue;
            }

            // Seeds run here, before the caller checks for schema drift (see EditorContext.Startup),
            // so a plugin's reference data lands before the migration gate ever sees the database.
            try
            {
                BlockingWork.Run(storage.ApplySeedsAsync);
            }
            catch (Exception e)
            {
                GD.PushError($"[Database] Storage '{storage.Name}' seed apply failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Every stored scene entity overlapping <paramref name="region"/>, asked of each storage's
    /// <see cref="ISceneEntityFactory"/>s under that storage's read lock. Reads storage rather than the
    /// loaded scene, so it answers for maps that are not open and regions nothing has streamed in —
    /// what offline work (a batch job, a landscape build for another map) needs.
    /// </summary>
    public async Task<IReadOnlyList<SceneEntity>> ScanSceneAsync(MapId map, Aabb region)
    {
        var result = new List<SceneEntity>();
        foreach (Storage storage in Storages)
        {
            using IDisposable reader = await storage.Lock.ReaderAsync().ConfigureAwait(false);
            foreach (ISceneEntityFactory factory in storage.SceneFactories)
            {
                IReadOnlyList<SceneEntity> loaded = await factory.ScanAsync(map, region).ConfigureAwait(false);
                result.AddRange(loaded);
            }
        }

        return result;
    }

    /// <summary>
    /// Loads a catalog whole into <see cref="EditorContext.Catalog"/>, replacing anything of that type
    /// already loaded — except an entity the active edit session still has pinned, which survives
    /// untouched (see <see cref="IsPinned"/>), so a type-scoped reload can never silently discard an
    /// uncommitted create or edit. Catalog lifetime belongs to whichever system owns the catalog —
    /// nothing streams these — so it calls this when it needs the set and
    /// <see cref="UnloadCatalog{TEntity}"/> when it is done.
    /// </summary>
    public IReadOnlyList<TEntity> LoadCatalog<TEntity>() where TEntity : CatalogEntity =>
        LoadCatalog(typeof(TEntity)).Cast<TEntity>().ToList();

    /// <summary>Type-based counterpart of <see cref="LoadCatalog{TEntity}"/> — what <see cref="IWorldParticipant.LoadWorld"/>
    /// uses to bulk-load every registered catalog type without naming each one.</summary>
    private IReadOnlyList<CatalogEntity> LoadCatalog(Type entityType)
    {
        _context.Catalog.RemoveAll(entityType, IsPinned);

        var loaded = new List<CatalogEntity>();
        foreach (Storage storage in Storages)
        {
            foreach (ICatalogEntityFactory factory in storage.CatalogFactories)
            {
                if (!entityType.IsAssignableFrom(factory.EntityType))
                {
                    continue;
                }

                try
                {
                    foreach (CatalogEntity entity in Read(storage, factory.LoadAllAsync))
                    {
                        if (entityType.IsInstanceOfType(entity))
                        {
                            _context.Catalog.Add(entity);
                            loaded.Add(entity);
                        }
                    }
                }
                catch (Exception e)
                {
                    GD.PushError($"[Database] Loading catalog {entityType.Name} from '{storage.Name}' failed: {e.Message}");
                }
            }
        }

        return loaded;
    }

    /// <summary>Drops a loaded catalog. Entities the edit session pinned stay alive until it ends.</summary>
    public void UnloadCatalog<TEntity>() where TEntity : CatalogEntity => _context.Catalog.RemoveAll<TEntity>(IsPinned);

    /// <summary>
    /// Every distinct <see cref="ICatalogEntityFactory.EntityType"/> registered across every storage —
    /// what <see cref="IWorldParticipant"/> loads/unloads as one project-wide bulk operation instead of
    /// each catalog's owning system (or, for a plugin catalog with no owning system, a bespoke
    /// <see cref="IWorldParticipant"/> written solely to shuttle it in and out) doing so itself.
    /// </summary>
    private IEnumerable<Type> CatalogEntityTypes() =>
        Storages.SelectMany(storage => storage.CatalogFactories).Select(factory => factory.EntityType).Distinct();

    // Loads before anything that resolves against a catalog (landscape channels, mesh material
    // presets, ...), and — since WorldLifecycle unloads in exact reverse — unloads only after every
    // other participant's UnloadWorld has already dropped whatever referenced them.
    float IWorldParticipant.LoadPriority => -1f;

    string? IWorldParticipant.LoadStep => "Loading catalogs";

    void IWorldParticipant.LoadWorld()
    {
        foreach (Type entityType in CatalogEntityTypes())
        {
            LoadCatalog(entityType);
        }
    }

    void IWorldParticipant.UnloadWorld()
    {
        foreach (Type entityType in CatalogEntityTypes())
        {
            _context.Catalog.RemoveAll(entityType, IsPinned);
        }
    }

    // Mirrors StreamingSystem.IsPinned — the same "is the active session still holding this for an
    // uncommitted edit" check, applied to a catalog entity instead of a scene one. Untyped (rather than
    // generic over TEntity) on purpose: Func<in T> is contravariant, so this satisfies
    // RemoveAll<TEntity>'s Func<TEntity, bool> for whatever TEntity the caller asks for.
    private bool IsPinned(CatalogEntity entity)
    {
        foreach (IEntity pinned in _context.EditSessions.Active.Pinned)
        {
            if (ReferenceEquals(pinned, entity))
            {
                return true;
            }
        }

        return false;
    }

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
                _context.ChunkChanges.RecordCommit(session, committed.Contains);
            }
            catch (Exception e)
            {
                GD.PushError($"[Database] Recording chunk changes failed: {e.Message}");
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

    // Whether the entity is still loaded, which is what separates a save from a delete.
    private bool IsLoaded(IEntity entity) => entity switch
    {
        SceneEntity scene => _context.Scene.Contains(scene),
        CatalogEntity catalog => _context.Catalog.Contains(catalog),
        _ => throw new ArgumentOutOfRangeException(nameof(entity), entity.GetType(), "Unknown entity kind."),
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
