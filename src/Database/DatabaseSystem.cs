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
