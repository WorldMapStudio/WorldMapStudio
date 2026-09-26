using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// An async reader/writer lock: any number of readers may hold it at once, but a writer holds it
/// exclusively. Scene streaming acquires the reader (concurrent DB reads); a commit acquires the
/// writer (exclusive). Await the acquire, then dispose the returned handle to release.
///
/// A held reader that is never disposed — a scan that hangs mid-await, say — blocks every later
/// writer forever with nothing in the log. Both acquires report themselves through <see cref="GD"/>
/// once they have waited past <see cref="WarnAfter"/>, then every <see cref="WarnInterval"/>,
/// naming how many readers are still in and how long the wait has run.
/// </summary>
public sealed class AsyncReaderWriterLock
{
    private static readonly TimeSpan WarnAfter = TimeSpan.FromSeconds(4.0);
    private static readonly TimeSpan WarnInterval = TimeSpan.FromSeconds(5.0);

    // A site that has already been warned about is not warned again — one line per offender is enough
    // to find it, and a hot path (a per-frame poll, say) would otherwise spam the log every call.
    private static readonly HashSet<string> WarnedSites = new();

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _gate = new();
    private int _readerCount;
    private TaskCompletionSource<bool>? _drained;

    /// <summary>How many readers hold the lock right now. For diagnostics only — inherently racy.</summary>
    public int ReaderCount => Volatile.Read(ref _readerCount);

    public async Task<IDisposable> ReaderAsync(
        CancellationToken cancellationToken = default,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "")
    {
        WarnIfMainThread(caller, file);

        // Briefly take the write lock so no reader can register while a writer holds it, then register.
        await WaitLogged(_writeLock.WaitAsync(cancellationToken), "reader").ConfigureAwait(false);
        lock (_gate)
        {
            _readerCount++;
        }

        _writeLock.Release();
        return new Releaser(this, writer: false);
    }

    public async Task<IDisposable> WriterAsync(
        CancellationToken cancellationToken = default,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "")
    {
        WarnIfMainThread(caller, file);

        await WaitLogged(_writeLock.WaitAsync(cancellationToken), "writer").ConfigureAwait(false);

        // Holding the write lock blocks new readers; now wait for existing readers to drain.
        while (true)
        {
            Task drained;
            lock (_gate)
            {
                if (_readerCount == 0)
                {
                    return new Releaser(this, writer: true);
                }

                _drained ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                drained = _drained.Task;
            }

            await WaitLogged(drained, "writer draining readers").ConfigureAwait(false);
        }
    }

    // Every affected call site should have hopped to the pool (BackgroundWork) or already be running
    // there (BlockingWork's own Task.Run) before it ever reaches the lock. Task.Run guarantees that hop
    // lands on a different real thread, so a lock request that is still on the main thread is a missed
    // one — see the async-db-plan for the inventory of sites this caught.
    [Conditional("DEBUG")]
    private static void WarnIfMainThread(string caller, string file)
    {
        if (!MainThread.IsCurrent)
        {
            return;
        }

        string site = $"{Path.GetFileNameWithoutExtension(file)}.{caller}";
        lock (WarnedSites)
        {
            if (!WarnedSites.Add(site))
            {
                return;
            }
        }

        GD.PushWarning($"[Lock] '{site}' acquired the lock on the main thread; it is missing a BackgroundWork hop.");
    }

    // Awaits an acquire, logging while it drags on. A leaked reader turns this from "a slow frame"
    // into "the editor is hung", so the wait has to announce itself rather than sit silent.
    private async Task WaitLogged(Task acquire, string role)
    {
        if (acquire.IsCompleted)
        {
            await acquire.ConfigureAwait(false);
            return;
        }

        long start = Stopwatch.GetTimestamp();
        TimeSpan gap = WarnAfter;
        while (true)
        {
            if (ReferenceEquals(await Task.WhenAny(acquire, Task.Delay(gap)).ConfigureAwait(false), acquire)
                || acquire.IsCompleted)
            {
                await acquire.ConfigureAwait(false);
                return;
            }

            GD.PushWarning(
                $"[Lock] {role} has waited {Stopwatch.GetElapsedTime(start).TotalSeconds:F1}s; "
                + $"{ReaderCount} reader(s) still hold the lock.");
            gap = WarnInterval;
        }
    }

    private void ReleaseReader()
    {
        lock (_gate)
        {
            if (--_readerCount == 0 && _drained != null)
            {
                _drained.SetResult(true);
                _drained = null;
            }
        }
    }

    private void ReleaseWriter() => _writeLock.Release();

    private sealed class Releaser(AsyncReaderWriterLock owner, bool writer) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            if (writer)
            {
                owner.ReleaseWriter();
            }
            else
            {
                owner.ReleaseReader();
            }
        }
    }
}
