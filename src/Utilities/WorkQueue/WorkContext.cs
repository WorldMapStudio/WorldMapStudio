using System;
using System.Runtime.CompilerServices;

namespace WorldMapStudio;

/// <summary>
/// Passed to a work body. Lets the work name its current step and hop between the background pool
/// and the main thread with <c>await</c>:
/// <code>
/// WorkQueue.Schedule("Import ADT", async ctx =>
/// {
///     ctx.Step("Reading file");            // runs on a background worker
///     byte[] data = ReadFromDisk();
///
///     await ctx.SwitchToMain();             // hop to the main thread
///     ctx.Step("Uploading to GPU");
///     UploadToGpu(data);
///
///     await ctx.SwitchToBackground();       // hop back to a worker
///     ctx.Step("Post-processing");
/// });
/// </code>
/// </summary>
public sealed class WorkContext
{
    internal WorkContext(WorkHandle handle)
    {
        Handle = handle;
    }

    internal WorkHandle Handle { get; }

    public string Name => Handle.Name;
    public bool IsCancellationRequested => Handle.IsCancellationRequested;

    /// <summary>Sets the label describing what this work is doing right now.</summary>
    public void Step(string step) => Handle.SetStep(step);

    public void ThrowIfCancellationRequested()
    {
        if (Handle.IsCancellationRequested)
        {
            throw new OperationCanceledException();
        }
    }

    /// <summary>Awaits until the continuation is resumed on the main thread (within its frame budget).</summary>
    public ThreadSwitchAwaitable SwitchToMain() => new(WorkThread.Main, Handle);

    /// <summary>Awaits until the continuation is resumed on a background worker.</summary>
    public ThreadSwitchAwaitable SwitchToBackground() => new(WorkThread.Background, Handle);

    /// <summary>
    /// Yields control on the current thread and re-queues the continuation. On the main thread this
    /// lets the frame budget breathe between chunks of work; on a worker it lets other work interleave.
    /// </summary>
    public ThreadSwitchAwaitable Yield() => new(Handle.CurrentThread, Handle);
}

/// <summary>
/// Awaitable returned by <see cref="WorkContext"/>. It never completes synchronously, so the async
/// state machine always re-queues its continuation onto the requested executor.
/// </summary>
public readonly struct ThreadSwitchAwaitable : INotifyCompletion
{
    private readonly WorkThread _target;
    private readonly WorkHandle _handle;

    internal ThreadSwitchAwaitable(WorkThread target, WorkHandle handle)
    {
        _target = target;
        _handle = handle;
    }

    public ThreadSwitchAwaitable GetAwaiter() => this;

    public bool IsCompleted => false;

    public void OnCompleted(Action continuation) =>
        WorkQueue.EnqueueContinuation(_target, continuation, _handle);

    public void GetResult() { }
}
