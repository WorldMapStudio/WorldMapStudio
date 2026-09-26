using System;
using System.Collections.Frozen;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Microsoft.Data.Sqlite;

namespace WorldMapStudio;

/// <summary>
/// Covers the one property <see cref="BackgroundWork"/> exists for: work it starts must not run under
/// whatever <see cref="SynchronizationContext"/> the calling thread carries — the Godot main thread's
/// context is exactly what let a database call that "should" hop to the pool run inline instead.
/// </summary>
public static class BackgroundWorkTests
{
    private static readonly Aabb Everywhere = new(new Vector3(-1.0e6f, -1.0e6f, -1.0e6f), new Vector3(2.0e6f, 2.0e6f, 2.0e6f));

    // Any distinct instance works: the test only checks that the running work does not see this one.
    private sealed class ProbeSynchronizationContext : SynchronizationContext
    {
    }

    private sealed class Scratch : IDisposable
    {
        private readonly string _databasePath;

        private Scratch(EditorContext context, string databasePath)
        {
            Context = context;
            _databasePath = databasePath;
        }

        public EditorContext Context { get; }

        public static Scratch Open()
        {
            string databasePath = Path.Combine(Path.GetTempPath(), $"__wms_background_work_test_{Guid.NewGuid():N}.doltlite");
            var context = new EditorContext(new Node3D(), new Project { Name = "__wms_background_work_test__" });
            context.Database.EditorStorage.Location.DatabasePath = databasePath;
            context.Database.EditorStorage.EnsureSchema();
            return new Scratch(context, databasePath);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
    }

    [EditorTest(Category = "Database", Thread = TestThread.Background)]
    public static async Task A_scan_started_through_BackgroundWork_does_not_run_under_the_callers_synchronization_context()
    {
        using Scratch scratch = Scratch.Open();
        EditorContext context = scratch.Context;

        var probe = new ProbeSynchronizationContext();
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(probe);
        try
        {
            SynchronizationContext? observed = probe;
            await BackgroundWork.Run(async () =>
            {
                observed = SynchronizationContext.Current;
                await context.Database.ScanFactoriesAsync(
                    new MapId(1), Everywhere, null, publishing: false,
                    FrozenDictionary<(string Source, long Key), int>.Empty).ConfigureAwait(false);
            }).ConfigureAwait(false);

            Assert.IsFalse(ReferenceEquals(observed, probe),
                "background work must escape the calling thread's synchronization context");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
