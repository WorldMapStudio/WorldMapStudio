using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace WorldMapStudio;

/// <summary>Covers <see cref="Storage.ApplySeedsAsync"/>'s once-only/retry-whole behaviour and the
/// DoltLite runtime check, independent of any concrete storage's own tables.</summary>
public static class StorageSeedTests
{
    private sealed class FakeSeed : ISeedSql
    {
        public required string Name { get; init; }
        public required string Sql { get; init; }
    }

    // A minimal Storage: only Seeds needs a body, so ApplySeedsAsync can be exercised without a
    // concrete storage's own tables or subsystem tree.
    private sealed class FakeStorage(IReadOnlyList<ISeedSql> seeds) : Storage
    {
        public override string Name => "FakeSeedStorage";

        public override IEnumerable<ISeedSql> Seeds => seeds;

        public static FakeStorage OpenAt(string path, params ISeedSql[] seeds)
        {
            var storage = new FakeStorage(seeds);
            storage.BindLocation(new StorageLocation { DatabasePath = path });
            return storage;
        }
    }

    private static async Task<long> TableCountAsync(string path, string tableName)
    {
        await using var connection = new SqliteConnection(new StorageLocation { DatabasePath = path }.BuildConnectionString());
        await connection.OpenAsync().ConfigureAwait(false);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE type='table' AND name=@n;";
        command.Parameters.AddWithValue("@n", tableName);
        return (long)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;
    }

    [EditorTest(Category = "Storage", Thread = TestThread.Background)]
    public static async Task A_seed_is_applied_once_and_not_reapplied()
    {
        string path = Path.Combine(Path.GetTempPath(), $"__wms_seed_once_test_{Guid.NewGuid():N}.doltlite");
        try
        {
            var seed = new FakeSeed { Name = "seed-once", Sql = "CREATE TABLE seeded (id INTEGER PRIMARY KEY);" };

            await FakeStorage.OpenAt(path, seed).ApplySeedsAsync().ConfigureAwait(false);
            await FakeStorage.OpenAt(path, seed).ApplySeedsAsync().ConfigureAwait(false);

            Assert.AreEqual(1L, await TableCountAsync(path, "seeded").ConfigureAwait(false));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [EditorTest(Category = "Storage", Thread = TestThread.Background)]
    public static async Task A_seed_that_fails_partway_leaves_nothing_and_is_retried_whole_next_time()
    {
        string path = Path.Combine(Path.GetTempPath(), $"__wms_seed_retry_test_{Guid.NewGuid():N}.doltlite");
        try
        {
            var failing = new FakeSeed
            {
                Name = "seed-retry",
                Sql = "CREATE TABLE partial (id INTEGER PRIMARY KEY); SELECT this_is_not_a_real_function();",
            };

            Exception? caught = null;
            try
            {
                await FakeStorage.OpenAt(path, failing).ApplySeedsAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                caught = e;
            }

            Assert.IsNotNull(caught, "the invalid statement should fail the seed");
            Assert.AreEqual(0L, await TableCountAsync(path, "partial").ConfigureAwait(false), "the failed transaction must roll back, not leave the table half-created");

            var fixedSeed = new FakeSeed { Name = "seed-retry", Sql = "CREATE TABLE partial (id INTEGER PRIMARY KEY);" };
            await FakeStorage.OpenAt(path, fixedSeed).ApplySeedsAsync().ConfigureAwait(false);

            Assert.AreEqual(1L, await TableCountAsync(path, "partial").ConfigureAwait(false), "not recorded as applied, so it must run again next startup");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [EditorTest(Category = "Storage", Thread = TestThread.Background)]
    public static async Task The_doltlite_runtime_check_passes_for_a_connection_doltlite_opened()
    {
        string path = Path.Combine(Path.GetTempPath(), $"__wms_doltlite_check_test_{Guid.NewGuid():N}.doltlite");
        try
        {
            DoltliteRuntime.EnsureInitialized();
            await using var connection = new SqliteConnection(new StorageLocation { DatabasePath = path }.BuildConnectionString());
            DoltliteRuntime.AssertDoltlite(connection);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
