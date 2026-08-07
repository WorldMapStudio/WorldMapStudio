using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        LoadScene();
    }

    /// <summary>Persists every entity touched by the session. One save per entity for now.</summary>
    public void Commit(EditSession session)
    {
        foreach (IEntity entity in session.Pinned)
        {
            if (entity is not SceneEntity scene)
            {
                continue;
            }

            ISceneEntityFactory? factory = FactoryFor(scene);
            if (factory == null)
            {
                continue;
            }

            try
            {
                factory.SaveAsync(scene).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                GD.PushError($"[Database] Failed to save {scene.DisplayName}: {e.Message}");
            }
        }
    }

    public ISceneEntityFactory? FactoryFor(SceneEntity entity)
    {
        foreach (Storage storage in Storages)
        {
            foreach (ISceneEntityFactory factory in storage.SceneFactories)
            {
                if (factory.Handles(entity))
                {
                    return factory;
                }
            }
        }

        return null;
    }

    // Loads each factory's persisted entities into the shared scene registry. Runs at startup on the
    // main thread; the entities are plain data (no Godot nodes) until the viewport represents them.
    private void LoadScene()
    {
        foreach (Storage storage in Storages)
        {
            foreach (ISceneEntityFactory factory in storage.SceneFactories)
            {
                try
                {
                    IReadOnlyList<SceneEntity> loaded = factory.LoadAllAsync().GetAwaiter().GetResult();
                    foreach (SceneEntity entity in loaded)
                    {
                        _context.Scene.Add(entity);
                    }
                }
                catch (Exception e)
                {
                    GD.PushError($"[Database] Failed to load entities for '{storage.Name}': {e.Message}");
                }
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

    private string DefaultDataDirectory()
    {
        string baseDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "WorldMapStudio", Sanitize(_context.Project.Name), "dolt");
    }

    private static string Sanitize(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name.Length == 0 ? "project" : name;
    }
}
