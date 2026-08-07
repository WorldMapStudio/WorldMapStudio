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

    /// <summary>Filesystem path to the dolt repository, used when <see cref="LaunchServer"/> is set.</summary>
    public string RepositoryPath { get; set; } = string.Empty;
}
