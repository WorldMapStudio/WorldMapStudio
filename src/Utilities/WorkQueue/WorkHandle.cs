using System;
using System.Diagnostics;

namespace WorldMapStudio;

/// <summary>Which executor a piece of work is running on (or waiting for).</summary>
public enum WorkThread
{
    Background,
    Main,
}

/// <summary>Lifecycle state of a scheduled work item.</summary>
public enum WorkState
{
    Queued,     // sitting in a queue, waiting for its next step to be picked up
    Executing,  // a step is running right now
    Completed,  // finished successfully
    Faulted,    // threw an exception
    Cancelled,  // cancellation was requested and observed
}

/// <summary>Immutable view of a <see cref="WorkHandle"/>, safe to read on any thread (e.g. the UI).</summary>
public readonly record struct WorkSnapshot(
    long Id,
    string Name,
    string Step,
    WorkState State,
    WorkThread Thread,
    double ElapsedSeconds,
    string Error)
{
    public bool IsActive => State is WorkState.Queued or WorkState.Executing;
    public bool IsFinished => !IsActive;
}

/// <summary>
/// Tracks a single scheduled work item. A work item has a <see cref="Name"/> that describes the
/// whole run and a <see cref="CurrentStep"/> that the work updates as it progresses. Instances are
/// created by <see cref="WorkQueue.Schedule(string, System.Func{WorkContext, System.Threading.Tasks.Task}, WorkThread)"/>.
/// </summary>
public sealed class WorkHandle
{
    private readonly object _lock = new();
    private readonly long _startTimestamp;

    private WorkState _state;
    private WorkThread _thread;
    private string _step = "";
    private string _error = "";
    private long _endTimestamp;
    private volatile bool _cancelRequested;

    internal WorkHandle(long id, string name, WorkThread startThread)
    {
        Id = id;
        Name = name;
        _thread = startThread;
        _state = WorkState.Queued;
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    public long Id { get; }
    public string Name { get; }

    public bool IsCancellationRequested => _cancelRequested;

    public WorkState State
    {
        get { lock (_lock) { return _state; } }
    }

    public WorkThread CurrentThread
    {
        get { lock (_lock) { return _thread; } }
    }

    public string CurrentStep
    {
        get { lock (_lock) { return _step; } }
    }

    /// <summary>Requests cooperative cancellation. The work must observe it via <see cref="WorkContext"/>.</summary>
    public void Cancel() => _cancelRequested = true;

    internal void SetStep(string step)
    {
        lock (_lock)
        {
            _step = step ?? "";
        }
    }

    internal void MarkQueued(WorkThread thread)
    {
        lock (_lock)
        {
            if (IsFinishedLocked())
            {
                return;
            }
            _state = WorkState.Queued;
            _thread = thread;
        }
    }

    internal void MarkExecuting(WorkThread thread)
    {
        lock (_lock)
        {
            if (IsFinishedLocked())
            {
                return;
            }
            _state = WorkState.Executing;
            _thread = thread;
        }
    }

    internal void Complete()
    {
        lock (_lock)
        {
            if (IsFinishedLocked())
            {
                return;
            }
            _state = WorkState.Completed;
            _endTimestamp = Stopwatch.GetTimestamp();
        }
    }

    internal void Fault(Exception exception)
    {
        lock (_lock)
        {
            if (IsFinishedLocked())
            {
                return;
            }
            _state = exception is OperationCanceledException ? WorkState.Cancelled : WorkState.Faulted;
            _error = exception?.Message ?? "";
            _endTimestamp = Stopwatch.GetTimestamp();
        }
    }

    internal WorkSnapshot Snapshot()
    {
        lock (_lock)
        {
            long end = _endTimestamp == 0 ? Stopwatch.GetTimestamp() : _endTimestamp;
            double elapsed = (end - _startTimestamp) / (double)Stopwatch.Frequency;
            return new WorkSnapshot(Id, Name, _step, _state, _thread, elapsed, _error);
        }
    }

    private bool IsFinishedLocked() =>
        _state is WorkState.Completed or WorkState.Faulted or WorkState.Cancelled;
}
