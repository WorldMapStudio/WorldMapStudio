using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace WorldMapStudio;

/// <summary>
/// Registry of <see cref="IBatchOperation"/>s and the machinery to run one behind
/// <see cref="WorldOperations"/>' exclusive gate, with progress, cancellation and an opt-in reload
/// request.
///
/// A plain member of <see cref="EditorContext"/> — the core spine, not an extension point — but
/// itself a host, so operations self-register into it. What an operation processes, and what it
/// skips as already done, is never this class's business.
/// </summary>
public sealed partial class BatchSystem : ISubsystemHost
{
    /// <summary>Enough that a script which started a run can still find it well after it finished.</summary>
    private const int RecentRunCapacity = 32;

    private readonly List<BatchRun> _runs = [];

    public BatchSystem(EditorContext context)
    {
        Context = context;
        InitializeSubsystems();
    }

    public EditorContext Context { get; }

    public IEnumerable<IBatchOperation> Operations => Subsystems.OfType<IBatchOperation>();

    /// <summary>Recent runs, newest first, capped.</summary>
    public IReadOnlyList<BatchRun> Runs => _runs;

    public IBatchOperation? Find(string operationId) =>
        Operations.FirstOrDefault(candidate => candidate.Id == operationId);

    public BatchRun? FindRun(long id) => _runs.FirstOrDefault(run => run.Id == id);

    /// <summary>
    /// Starts an operation, or returns null with <paramref name="blocker"/> set to why it cannot start
    /// — another exclusive operation running, a reload pending, or the edit session dirty. Only one
    /// batch runs at a time, whatever it claims to do.
    /// </summary>
    public BatchRun? TryStart(IBatchOperation operation, JsonObject? overrides, out string? blocker)
    {
        JsonObject settings = MergeSettings(operation.SaveSettings(), overrides);
        var status = new BatchStatusHolder();
        var reload = new BatchReloadLatch();

        WorkHandle? handle = Context.Operations.TryRun(
            operation.DisplayName,
            async work =>
            {
                var context = new BatchContext(this, settings, status, reload, work);
                try
                {
                    await operation.RunAsync(context, work).ConfigureAwait(false);
                }
                finally
                {
                    // Outside the success path on purpose: the latch is set when a write lands, so a
                    // run that wrote and then faulted still leaves the world needing a re-read.
                    if (reload.Reason is { } reason)
                    {
                        Context.RequestReload($"{operation.DisplayName}: {reason}");
                    }
                }
            },
            out blocker,
            // WorldOperations would reload unconditionally; a batch decides for itself, through the latch.
            reloadAfter: false);

        if (handle == null)
        {
            return null;
        }

        var run = new BatchRun(operation.Id, handle, status, reload);
        _runs.Insert(0, run);
        while (_runs.Count > RecentRunCapacity)
        {
            _runs.RemoveAt(_runs.Count - 1);
        }

        return run;
    }

    /// <summary>The persisted settings with per-run overrides on top, as one immutable blob.</summary>
    private static JsonObject MergeSettings(JsonObject settings, JsonObject? overrides)
    {
        JsonObject merged = settings.DeepClone().AsObject();
        if (overrides == null)
        {
            return merged;
        }

        foreach ((string key, JsonNode? value) in overrides)
        {
            merged[key] = value?.DeepClone();
        }

        return merged;
    }
}
