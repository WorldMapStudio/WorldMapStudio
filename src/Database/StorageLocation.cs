using Microsoft.Data.Sqlite;

namespace WorldMapStudio;

/// <summary>
/// Where a <see cref="Storage"/> keeps its data: a DoltLite file and, optionally, the branch to open
/// it on. Configured per storage and persisted with the project.
/// </summary>
public sealed class StorageLocation
{
    /// <summary>
    /// Path to the DoltLite database file. A relative path resolves against the project folder.
    /// Empty means the database system fills in a per-project default.
    /// </summary>
    public string DatabasePath { get; set; } = string.Empty;

    /// <summary>Branch to open (<c>file@branch</c>). Empty opens the file's default branch.</summary>
    public string Branch { get; set; } = string.Empty;

    /// <summary>Builds a SQLite connection string pointed at <see cref="DatabasePath"/>, on <see cref="Branch"/> if set.</summary>
    public string BuildConnectionString()
    {
        string dataSource = Branch.Length > 0 ? $"{DatabasePath}@{Branch}" : DatabasePath;
        return new SqliteConnectionStringBuilder
        {
            DataSource = dataSource,
            Pooling = true,
            ForeignKeys = true,
        }.ConnectionString;
    }
}
