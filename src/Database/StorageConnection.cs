using MySqlConnector;

namespace WorldMapStudio;

/// <summary>
/// How a <see cref="Storage"/> reaches its dolt database. The editor can either launch a
/// <c>dolt sql-server</c> for the repository itself or connect to an already-running server; this is
/// configured per storage. Persisted with the project (project persistence is not wired up yet, so
/// for now these live in memory with their defaults).
/// </summary>
public sealed class StorageConnection
{
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 3306;

    public string Database { get; set; } = string.Empty;

    public string User { get; set; } = "root";

    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// When true the editor starts and owns a <c>dolt sql-server</c> for <see cref="RepositoryPath"/>;
    /// otherwise it connects to a server someone else is running.
    /// </summary>
    public bool LaunchServer { get; set; }

    /// <summary>
    /// Data directory a launched <c>dolt sql-server</c> serves (each database is a subdirectory).
    /// Used when <see cref="LaunchServer"/> is set; empty means the database system fills in a
    /// per-project default.
    /// </summary>
    public string RepositoryPath { get; set; } = string.Empty;

    /// <summary>Builds a MySQL connection string, optionally without a database (to run CREATE DATABASE).</summary>
    public string BuildConnectionString(bool includeDatabase = true)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = Host,
            Port = (uint)Port,
            UserID = User,
            Password = Password,
        };

        if (includeDatabase && Database.Length > 0)
        {
            builder.Database = Database;
        }

        return builder.ConnectionString;
    }
}
