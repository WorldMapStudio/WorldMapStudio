using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// Exclusive ownership of the editor, held open across many calls. The one gate-holding concept in the
/// system: an <see cref="IBatchOperation"/> run is a session whose body is the operation, and a
/// script-driven batch is a session whose body is "wait until told to stop".
///
/// Sessions exist because a script cannot *be* a multi-minute batch body — the script host evaluates
/// on the main thread under a statement timeout — but it can drive one across many calls.
///
/// <b>Nothing ever ends a session on a timer</b>, at any duration. A batch that takes minutes is the
/// normal case, and a session idle between calls is doing exactly what a script-driven batch is
/// supposed to do; to a timer that is indistinguishable from a dead client, so any timeout is a guess
/// about someone else's workload whose failure mode is killing real work. <see cref="LastActivityUtc"/>
/// is there so the person reading the batch window can tell the difference, and enforces nothing —
/// the recovery path for a wedged session is <see cref="BatchSystem.ForceEnd"/>, one click away.
/// </summary>
public sealed class BatchSession : IDisposable
{
    private readonly BatchStatusHolder _status = new();
    private readonly BatchReloadLatch _reload = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _lock = new();

    private WorkContext? _work;
    private WorkHandle? _handle;
    private DateTime _lastActivityUtc;

    internal BatchSession(BatchSystem batch, string name, JsonObject settings, BatchOperationState state)
    {
        Batch = batch;
        Name = name;
        OpenedUtc = DateTime.UtcNow;
        _lastActivityUtc = OpenedUtc;
        Context = new BatchContext(this, settings, state);
    }

    internal BatchSystem Batch { get; }

    public string Name { get; }

    /// <summary>Everything the session can do to the editor.</summary>
    public BatchContext Context { get; }

    public DateTime OpenedUtc { get; }

    /// <summary>When a <see cref="BatchContext"/> call last came in. Display only — see the class
    /// remarks for why nothing acts on it.</summary>
    public DateTime LastActivityUtc
    {
        get
        {
            lock (_lock)
            {
                return _lastActivityUtc;
            }
        }
    }

    /// <summary>The work item holding the gate, so a session shows in the Work Queue window.</summary>
    public WorkHandle? Work
    {
        get
        {
            lock (_lock)
            {
                return _handle;
            }
        }
    }

    public bool IsOpen => !_completion.Task.IsCompleted;

    /// <summary>Whether something in this session has written behind the loaded world's back.</summary>
    public bool ReloadRequired => _reload.Reason != null;

    internal string? ReloadReason => _reload.Reason;

    /// <summary>A snapshot taken under the status lock — safe to read every frame from the main thread
    /// while a worker writes it.</summary>
    public BatchStatus Status() => _status.Snapshot();

    /// <summary>Releases the gate. A reload is requested if anything latched one. Idempotent.</summary>
    public void End() => _completion.TrySetResult();

    /// <summary>Asks whatever is running in this session to stop. Cooperative, like any other work
    /// cancellation, so it does nothing for a session that is only sitting idle between calls —
    /// <see cref="End"/> is what releases those.</summary>
    public void Cancel()
    {
        lock (_lock)
        {
            _handle?.Cancel();
        }
    }

    void IDisposable.Dispose() => End();

    internal Task Completion => _completion.Task;

    /// <summary>The work item this session's body is running under — what a context call needs to hop
    /// threads. Null only before the body starts.</summary>
    internal WorkContext? WorkContext
    {
        get
        {
            lock (_lock)
            {
                return _work;
            }
        }
    }

    internal void BindWork(WorkContext work)
    {
        lock (_lock)
        {
            _work = work;
        }
    }

    internal void BindHandle(WorkHandle handle)
    {
        lock (_lock)
        {
            _handle = handle;
        }
    }

    internal void Touch()
    {
        lock (_lock)
        {
            _lastActivityUtc = DateTime.UtcNow;
        }
    }

    internal void Step(string step)
    {
        Touch();
        _status.Step(step);

        // Mirrored onto the work handle so the Work Queue window and the batch window never disagree.
        lock (_lock)
        {
            _work?.Step(step);
        }
    }

    internal void Progress(float fraction)
    {
        Touch();
        _status.Progress(fraction);
    }

    internal void Report(string message)
    {
        Touch();
        _status.Report(message);
    }

    internal void Log(string line)
    {
        Touch();
        _status.Log(line);
    }

    internal void RequireReload(string reason)
    {
        Touch();
        _reload.Require(reason);
    }
}
