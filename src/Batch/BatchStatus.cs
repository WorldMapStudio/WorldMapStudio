using System;
using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// What a batch is doing right now, as an immutable snapshot. <see cref="WorkContext.Step"/> alone is
/// one overwritten line with no sense of how far along anything is, which is too thin for a run that
/// lasts minutes.
/// </summary>
/// <param name="Progress">0..1, or null when the operation genuinely cannot say — the window draws an
/// indeterminate bar for null rather than one pinned at empty.</param>
public readonly record struct BatchStatus(
    string Step,
    float? Progress,
    string Message,
    IReadOnlyList<string> Log);

/// <summary>
/// The mutable half of <see cref="BatchStatus"/>. Written from a worker and read from the main thread
/// every frame, so every field is behind one lock and <see cref="Snapshot"/> hands out a copy — never
/// the live queue, which is how a reader gets a collection-modified exception mid-append.
/// </summary>
internal sealed class BatchStatusHolder
{
    /// <summary>An operation that logs per chunk would otherwise grow the log for the whole run.</summary>
    private const int LogCapacity = 500;

    private readonly object _lock = new();
    private readonly Queue<string> _log = new();

    private string _step = "";
    private float? _progress;
    private string _message = "";
    private string[] _logSnapshot = [];
    private bool _logDirty;

    public void Step(string step)
    {
        lock (_lock)
        {
            _step = step;
        }
    }

    public void Progress(float fraction)
    {
        lock (_lock)
        {
            _progress = Math.Clamp(fraction, 0.0f, 1.0f);
        }
    }

    public void Report(string message)
    {
        lock (_lock)
        {
            _message = message;
        }
    }

    public void Log(string line)
    {
        lock (_lock)
        {
            _log.Enqueue(line);
            while (_log.Count > LogCapacity)
            {
                _log.Dequeue();
            }

            _logDirty = true;
        }
    }

    /// <summary>Rebuilds the log array only when <see cref="Log"/> actually added a line since the last
    /// call — a batch logs a handful of times a second at most, far slower than the every-frame poll
    /// this feeds, so most calls hand back the same array rather than paying for a fresh
    /// <see cref="LogCapacity"/>-element copy every frame.</summary>
    public BatchStatus Snapshot()
    {
        lock (_lock)
        {
            if (_logDirty)
            {
                _logSnapshot = _log.ToArray();
                _logDirty = false;
            }

            return new BatchStatus(_step, _progress, _message, _logSnapshot);
        }
    }
}

/// <summary>
/// A batch's "the world needs reloading" latch. Set the moment a write lands rather than returned at
/// the end, so a run that faults halfway through having already written still reloads.
/// </summary>
internal sealed class BatchReloadLatch
{
    private readonly object _lock = new();
    private string? _reason;

    /// <summary>The first reason wins, the way <see cref="EditorContext.RequestReload"/> does.</summary>
    public void Require(string reason)
    {
        lock (_lock)
        {
            _reason ??= reason;
        }
    }

    public string? Reason
    {
        get
        {
            lock (_lock)
            {
                return _reason;
            }
        }
    }
}
