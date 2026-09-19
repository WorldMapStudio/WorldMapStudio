using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Godot;
using Timer = System.Threading.Timer;

namespace WorldMapStudio;

/// <summary>
/// Runs an async database call to completion synchronously, stalling the caller until it finishes.
///
/// <b>Why <c>Task.Run</c> rather than awaiting directly:</b> blocking on an async call from a thread
/// that carries a <see cref="System.Threading.SynchronizationContext"/> — the Godot main thread does —
/// deadlocks, because a continuation tries to resume on the very thread that is blocked. Wrapping in
/// <see cref="Task.Run(Func{Task})"/> keeps the whole chain on the thread pool, where nothing needs
/// the blocked thread back. The block is what is left over after dodging the deadlock, not the point.
///
/// <b>What it costs:</b> called from the main thread, this is a dropped frame for as long as the query
/// takes. That is tolerable for the discrete acts it is used for — opening a project, switching map,
/// creating a map, committing a session — and not for anything per-frame. Use <see cref="WorkQueue"/>
/// for work that should not be felt.
///
/// Deliberately one shared helper: every main-thread database stall in the editor goes through here,
/// so this is the one place to grep, count, or replace.
///
/// <b>Watchdog:</b> a stall here shows up as a frozen window with nothing in the log. Each call arms
/// an off-thread timer that names the calling site and how long it has been blocked, first after
/// <see cref="WarnAfter"/> and then every <see cref="WarnInterval"/>, so an indefinite freeze
/// (a lock that never comes back) leaves a trail pointing at which act stalled and for how long.
/// </summary>
public static class BlockingWork
{
    // A DB act that hasn't returned by here is no longer "a slow query" — it is waiting on something
    // that may never arrive. Short enough to catch the freeze, long enough that a genuinely heavy
    // one-off (a migration, a big commit) doesn't cry wolf every run.
    private static readonly TimeSpan WarnAfter = TimeSpan.FromSeconds(4.0);
    private static readonly TimeSpan WarnInterval = TimeSpan.FromSeconds(5.0);

    /// <summary>Runs the work and returns its result, blocking until it completes.</summary>
    public static T Run<T>(Func<Task<T>> work, [CallerMemberName] string caller = "", [CallerFilePath] string file = "")
    {
        using var watchdog = new Watchdog(caller, file);
        return Task.Run(work).GetAwaiter().GetResult();
    }

    /// <summary>Runs the work, blocking until it completes.</summary>
    public static void Run(Func<Task> work, [CallerMemberName] string caller = "", [CallerFilePath] string file = "")
    {
        using var watchdog = new Watchdog(caller, file);
        Task.Run(work).GetAwaiter().GetResult();
    }

    // Reports the still-blocked calling site from a timer thread — it cannot use the main thread to
    // report, because that is the thread stuck in Run. Logging through GD off-thread is already how
    // the editor reports background failures (see WorldReload's load task).
    private sealed class Watchdog : IDisposable
    {
        private readonly string _site;
        private readonly long _start;
        private readonly Timer _timer;

        public Watchdog(string caller, string file)
        {
            _site = $"{Path.GetFileNameWithoutExtension(file)}.{caller}";
            _start = Stopwatch.GetTimestamp();
            _timer = new Timer(_ => Warn(), null, WarnAfter, WarnInterval);
        }

        private double Elapsed => Stopwatch.GetElapsedTime(_start).TotalSeconds;

        private void Warn() =>
            GD.PushWarning($"[BlockingWork] '{_site}' has stalled the main thread for {Elapsed:F1}s.");

        public void Dispose()
        {
            _timer.Dispose();
            double elapsed = Elapsed;
            if (elapsed >= WarnAfter.TotalSeconds)
            {
                GD.Print($"[BlockingWork] '{_site}' returned after {elapsed:F1}s.");
            }
        }
    }
}
