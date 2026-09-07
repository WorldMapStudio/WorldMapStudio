using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

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
public sealed partial class BatchSystem : ISubsystemHost, IWorldParticipant
{
    /// <summary>Enough that a script which started a run can still find it well after it finished.</summary>
    private const int RecentRunCapacity = 32;

    private readonly List<BatchRun> _runs = [];

    // The persisted settings blob per operation, so a run can snapshot it without a query. Kept in
    // step by SaveSettings, which is the only thing that writes the stored copy.
    private readonly Dictionary<string, JsonObject> _settings = [];

    public BatchSystem(EditorContext context)
    {
        Context = context;
        State = new BatchState(context);
        InitializeSubsystems();
    }

    public EditorContext Context { get; }

    /// <summary>Where operations keep whatever they need to remember between runs.</summary>
    public BatchState State { get; }

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
        JsonObject settings = MergeSettings(SettingsFor(operation.Id), overrides);
        var status = new BatchStatusHolder();
        var reload = new BatchReloadLatch();

        WorkHandle? handle = Context.Operations.TryRun(
            operation.DisplayName,
            async work =>
            {
                var context = new BatchContext(this, settings, State.For(operation.Id), status, reload, work);
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

    /// <summary>An operation's stored settings blob, or an empty one when it has never been saved.</summary>
    public JsonObject SettingsFor(string operationId) =>
        _settings.TryGetValue(operationId, out JsonObject? blob) ? blob : new JsonObject();

    /// <summary>Captures an operation's current fields as its persisted settings. Called by the window
    /// when the user changes something; a run reads the result, never the fields themselves.</summary>
    public void SaveSettings(IBatchOperation operation)
    {
        JsonObject blob = operation.SaveSettings();
        _settings[operation.Id] = blob;

        try
        {
            BlockingWork.Run(() => State.System(operation.Id).SetAsync(BatchState.SettingsKey, blob.ToJsonString()));
        }
        catch (Exception e)
        {
            GD.PushError($"[Batch] Saving settings for '{operation.Id}' failed: {e.Message}");
        }
    }

    // Settings are project data, so they wait on the database and the migration gate like any other
    // participant's load rather than being read in the constructor.
    void IWorldParticipant.LoadWorld()
    {
        _settings.Clear();
        foreach (IBatchOperation operation in Operations)
        {
            JsonObject blob = LoadSettings(operation.Id);
            _settings[operation.Id] = blob;
            operation.LoadSettings(blob);
        }
    }

    void IWorldParticipant.UnloadWorld() => _settings.Clear();

    private JsonObject LoadSettings(string operationId)
    {
        try
        {
            string? stored = BlockingWork.Run(() => State.System(operationId).GetAsync(BatchState.SettingsKey));
            if (stored != null && JsonNode.Parse(stored) is JsonObject blob)
            {
                return blob;
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            GD.PushError($"[Batch] Loading settings for '{operationId}' failed: {e.Message}");
        }

        return new JsonObject();
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
