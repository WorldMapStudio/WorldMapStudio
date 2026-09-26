using System;
using DoltHub.Doltlite;
using Microsoft.Data.Sqlite;

namespace WorldMapStudio;

/// <summary>
/// One-time process-wide setup for DoltLite: registers it with SQLitePCLRaw so every
/// <see cref="SqliteConnection"/> opened afterwards runs against DoltLite rather than stock SQLite.
/// </summary>
public static class DoltliteRuntime
{
    private static bool _initialized;
    private static readonly object Gate = new();

    public static void EnsureInitialized()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            Doltlite.Init();
            _initialized = true;
        }
    }

    /// <summary>
    /// Confirms the given connection's SQLite engine is actually DoltLite, not the stock engine a
    /// stray <c>Microsoft.EntityFrameworkCore.Sqlite</c> reference could silently fall back to.
    /// Stock SQLite still opens a DoltLite-format file (its headers fall back to the B-tree engine),
    /// so this has to be checked rather than inferred from the file opening successfully.
    /// </summary>
    public static void AssertDoltlite(SqliteConnection connection)
    {
        bool wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed)
        {
            connection.Open();
        }

        try
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT dolt_version();";
            command.ExecuteScalar();
        }
        catch (SqliteException e)
        {
            throw new InvalidOperationException(
                "This connection is not running on DoltLite — check that Microsoft.EntityFrameworkCore.Sqlite.Core " +
                "(not the non-Core package) is referenced and that Doltlite.Init() ran first.", e);
        }
        finally
        {
            if (wasClosed)
            {
                connection.Close();
            }
        }
    }
}
