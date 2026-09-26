using System;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// Starts an async database call on the thread pool so the main thread cannot run it inline.
/// <c>Microsoft.Data.Sqlite</c> has no real async I/O — every <c>...Async</c> call it backs finishes
/// synchronously and returns an already-completed task, so a call started from the main thread never
/// yields to it. Every background database read goes through here, the same way every main-thread
/// stall goes through <see cref="BlockingWork"/> — grepping either name lists every site of its kind.
/// </summary>
public static class BackgroundWork
{
    public static Task<T> Run<T>(Func<Task<T>> work) => Task.Run(work);

    public static Task Run(Func<Task> work) => Task.Run(work);
}
