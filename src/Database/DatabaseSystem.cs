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
public sealed partial class DatabaseSystem : ISubsystemHost
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
    /// Persists everything the session touched, grouped per storage into one transaction each. A
    /// pinned entity still in the scene registry is saved; one that has left it (an undone creation
    /// or a deletion) is deleted.
    /// </summary>
    public void Commit(EditSession session)
    {
        foreach (Storage storage in Storages)
        {
            var saves = new List<SceneEntity>();
            var deletes = new List<SceneEntity>();

            foreach (IEntity entity in session.Pinned)
            {
                if (entity is SceneEntity scene && storage.SceneFactories.Any(factory => factory.Handles(scene)))
                {
                    (_context.Scene.Contains(scene) ? saves : deletes).Add(scene);
                }
            }

            if (saves.Count == 0 && deletes.Count == 0)
            {
                continue;
            }

            try
            {
                // Run on the thread pool, not the Godot main thread: blocking on an async DB call from
                // a thread that carries a SynchronizationContext deadlocks when a continuation tries to
                // resume on the (blocked) main thread. Task.Run keeps the whole chain off that context.
                Task.Run(() => storage.CommitAsync(saves, deletes)).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                GD.PushError($"[Database] Commit failed for '{storage.Name}': {e.Message}");
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
