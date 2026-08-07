using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Central work scheduler. Background steps run on a small pool of worker threads; main-thread steps
/// run from <see cref="PumpMainThread"/> under a per-frame time budget so they never stall rendering.
/// A single work item can freely bounce between the two via <see cref="WorkContext"/>.
///
/// Every scheduled item self-registers, so the system can report how much work is queued/executing,
/// what kind (by name), and which step each item is on. See <see cref="WorkQueueWindow"/> for the UI.
/// </summary>
public static class WorkQueue
{
    private readonly record struct Job(Action Continuation, WorkHandle Handle, WorkThread Thread);

    private const int MaxFinishedRetained = 128;

    private static readonly BlockingCollection<Job> _background = new(new ConcurrentQueue<Job>());
    private static readonly ConcurrentQueue<Job> _main = new();

    private static readonly List<Thread> _workers = new();
    private static readonly object _registryLock = new();
    private static readonly List<WorkHandle> _handles = new();

    private static long _nextId;
    private static bool _initialized;

    public static int WorkerCount { get; private set; }

    /// <summary>
    /// Starts the background worker threads. Called automatically on the first <see cref="Schedule"/>,
    /// but can be called explicitly to control the worker count. Idempotent.
    /// </summary>
    public static void Initialize(int workerCount = 0)
    {
        lock (_registryLock)
        {
            if (_initialized)
            {
                return;
            }
            _initialized = true;

            if (workerCount <= 0)
            {
                workerCount = Math.Max(1, System.Environment.ProcessorCount - 1);
            }
            WorkerCount = workerCount;

            for (int i = 0; i < workerCount; i++)
            {
                Thread thread = new(WorkerLoop)
                {
                    Name = $"WorkQueue-{i}",
                    IsBackground = true,
                };
                _workers.Add(thread);
                thread.Start();
            }
        }
    }

    /// <summary>Stops accepting new background work and lets worker threads drain and exit.</summary>
    public static void Shutdown()
    {
        lock (_registryLock)
        {
            if (!_initialized)
            {
                return;
            }
            _initialized = false;
        }
        _background.CompleteAdding();
    }

    /// <summary>Schedules an async work body. Use <see cref="WorkContext"/> to switch threads and set steps.</summary>
    public static WorkHandle Schedule(string name, Func<WorkContext, Task> body, WorkThread startOn = WorkThread.Background)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!_initialized)
        {
            Initialize();
        }

        WorkHandle handle = new(Interlocked.Increment(ref _nextId), string.IsNullOrEmpty(name) ? "Work" : name, startOn);
        Register(handle);

        WorkContext ctx = new(handle);
        EnqueueContinuation(startOn, () => RunBody(handle, body, ctx), handle);
        return handle;
    }

    /// <summary>Convenience overload for a synchronous body (still gets a <see cref="WorkContext"/> for steps/cancellation).</summary>
    public static WorkHandle Schedule(string name, Action<WorkContext> body, WorkThread startOn = WorkThread.Background)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Schedule(name, ctx =>
        {
            body(ctx);
            return Task.CompletedTask;
        }, startOn);
    }

    /// <summary>
    /// Drains the main-thread queue until <paramref name="budgetMilliseconds"/> is exceeded. Call once
    /// per frame from the main thread. Leftover work carries over to the next frame.
    /// </summary>
    public static void PumpMainThread(double budgetMilliseconds = 4.0)
    {
        long start = Stopwatch.GetTimestamp();
        double budgetTicks = budgetMilliseconds * Stopwatch.Frequency / 1000.0;

        while (_main.TryDequeue(out Job job))
        {
            RunJob(job);
            if (Stopwatch.GetTimestamp() - start >= budgetTicks)
            {
                break;
            }
        }
    }

    /// <summary>Snapshot of every tracked item (active plus recently finished), safe to read on the UI thread.</summary>
    public static WorkSnapshot[] Snapshot()
    {
        lock (_registryLock)
        {
            WorkSnapshot[] result = new WorkSnapshot[_handles.Count];
            for (int i = 0; i < _handles.Count; i++)
            {
                result[i] = _handles[i].Snapshot();
            }
            return result;
        }
    }

    /// <summary>Requests cancellation of a tracked item by id.</summary>
    public static void Cancel(long id)
    {
        lock (_registryLock)
        {
            foreach (WorkHandle handle in _handles)
            {
                if (handle.Id == id)
                {
                    handle.Cancel();
                    return;
                }
            }
        }
    }

    /// <summary>Drops finished items from the registry.</summary>
    public static void ClearFinished()
    {
        lock (_registryLock)
        {
            _handles.RemoveAll(h => h.State is not WorkState.Queued and not WorkState.Executing);
        }
    }

    internal static void EnqueueContinuation(WorkThread thread, Action continuation, WorkHandle handle)
    {
        handle.MarkQueued(thread);
        Job job = new(continuation, handle, thread);
        if (thread == WorkThread.Main)
        {
            _main.Enqueue(job);
        }
        else
        {
            try
            {
                _background.Add(job);
            }
            catch (InvalidOperationException)
            {
                // Adding completed during shutdown; run the continuation inline so the item still finishes.
                RunJob(job);
            }
        }
    }

    private static async void RunBody(WorkHandle handle, Func<WorkContext, Task> body, WorkContext ctx)
    {
        try
        {
            await body(ctx);
            handle.Complete();
        }
        catch (OperationCanceledException)
        {
            handle.Fault(new OperationCanceledException());
        }
        catch (Exception e)
        {
            handle.Fault(e);
            GD.PushError($"[WorkQueue] '{handle.Name}' faulted: {e}");
        }
    }

    private static void WorkerLoop()
    {
        try
        {
            foreach (Job job in _background.GetConsumingEnumerable())
            {
                RunJob(job);
            }
        }
        catch (InvalidOperationException)
        {
            // CompleteAdding raced with a consumer; treated as a clean shutdown.
        }
    }

    private static void RunJob(Job job)
    {
        job.Handle.MarkExecuting(job.Thread);
        try
        {
            job.Continuation();
        }
        catch (Exception e)
        {
            // Continuations resume async state machines that capture their own exceptions, so reaching
            // here is unexpected; record it rather than tearing down the worker.
            job.Handle.Fault(e);
            GD.PushError($"[WorkQueue] '{job.Handle.Name}' step threw: {e}");
        }
    }

    private static void Register(WorkHandle handle)
    {
        lock (_registryLock)
        {
            _handles.Add(handle);
            PruneFinishedLocked();
        }
    }

    private static void PruneFinishedLocked()
    {
        int finished = 0;
        foreach (WorkHandle handle in _handles)
        {
            if (handle.State is not WorkState.Queued and not WorkState.Executing)
            {
                finished++;
            }
        }

        if (finished <= MaxFinishedRetained)
        {
            return;
        }

        int toRemove = finished - MaxFinishedRetained;
        for (int i = 0; i < _handles.Count && toRemove > 0;)
        {
            if (_handles[i].State is not WorkState.Queued and not WorkState.Executing)
            {
                _handles.RemoveAt(i);
                toRemove--;
            }
            else
            {
                i++;
            }
        }
    }
}
