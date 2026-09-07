namespace WorldMapStudio;

/// <summary>
/// One started <see cref="IBatchOperation"/>. Backed by a <see cref="WorkQueue"/> item, so a batch
/// shows up in the Work Queue window like any other work.
///
/// A cancelled or faulted run leaves whatever it had already written and is not resumable: nothing
/// here checkpoints progress or rolls anything back. Redoing work is the cheap failure.
/// </summary>
public sealed class BatchRun
{
    private readonly BatchStatusHolder _status;
    private readonly BatchReloadLatch _reload;

    internal BatchRun(string operationId, WorkHandle work, BatchStatusHolder status, BatchReloadLatch reload)
    {
        OperationId = operationId;
        Work = work;
        _status = status;
        _reload = reload;
    }

    public long Id => Work.Id;

    public string OperationId { get; }

    public WorkHandle Work { get; }

    /// <summary>Whether the operation has said it wrote something the loaded world needs to re-read.</summary>
    public bool ReloadRequired => _reload.Reason != null;

    /// <summary>A snapshot taken under the status lock — safe to call every frame from the main thread
    /// while the run writes it from a worker.</summary>
    public BatchStatus Status() => _status.Snapshot();

    /// <summary>Requests cooperative cancellation. The operation observes it through
    /// <see cref="WorkContext.ThrowIfCancellationRequested"/>.</summary>
    public void Cancel() => Work.Cancel();
}
