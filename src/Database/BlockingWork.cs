using System;
using System.Threading.Tasks;

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
/// Deliberately one shared helper. It was previously copy-pasted, with the same comment, into four
/// systems, which made the stalls impossible to find and guaranteed the fifth system would paste it
/// again. Every main-thread database stall in the editor goes through here, so this is the one place
/// to grep, count, or later replace.
/// </summary>
public static class BlockingWork
{
    /// <summary>Runs the work and returns its result, blocking until it completes.</summary>
    public static T Run<T>(Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();

    /// <summary>Runs the work, blocking until it completes.</summary>
    public static void Run(Func<Task> work) => Task.Run(work).GetAwaiter().GetResult();
}
