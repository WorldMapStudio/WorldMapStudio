using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// One started <see cref="IBatchOperation"/> — a <see cref="BatchSession"/> whose body is the
/// operation. Backed by a <see cref="WorkQueue"/> item, so a batch shows up in the Work Queue window
/// like any other work.
///
/// A cancelled or faulted run leaves whatever it had already written and is not resumable: nothing
/// here checkpoints progress or rolls anything back. Redoing work is the cheap failure.
/// </summary>
public sealed class BatchRun
{
    private readonly BatchSession _session;

    internal BatchRun(string operationId, BatchSession session)
    {
        OperationId = operationId;
        _session = session;
    }

    public long Id => _session.Work?.Id ?? 0L;

    public string OperationId { get; }

    public WorkHandle? Work => _session.Work;

    /// <summary>Whether the operation has said it wrote something the loaded world needs to re-read.</summary>
    public bool ReloadRequired => _session.ReloadRequired;

    /// <summary>A snapshot taken under the status lock — safe to call every frame from the main thread
    /// while the run writes it from a worker.</summary>
    public BatchStatus Status() => _session.Status();

    /// <summary>Whether any phase has been measured yet — cheap enough to poll every frame, unlike
    /// <see cref="Timings"/>, which sorts and formats the whole table under the same lock the run's
    /// worker threads take to record a phase.</summary>
    public bool HasTimings => !_session.Timings.IsEmpty;

    /// <summary>Where this run's wall clock went, as log-ready lines. Readable while the run is still
    /// going, so a multi-minute batch can be asked what it is spending its time on without waiting for
    /// it to finish.</summary>
    public IReadOnlyList<string> Timings() => _session.Timings.Format($"{OperationId} phase breakdown");

    /// <summary>Requests cooperative cancellation. The operation observes it through
    /// <see cref="WorkContext.ThrowIfCancellationRequested"/>.</summary>
    public void Cancel() => _session.Cancel();
}
