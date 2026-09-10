using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
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

    /// <summary>The session currently holding the gate, whether a run's or a script's.</summary>
    public BatchSession? ActiveSession { get; private set; }

    /// <summary>
    /// Starts an operation, or returns null with <paramref name="blocker"/> set to why it cannot start
    /// — another exclusive operation running, a reload pending, or the edit session dirty. Only one
    /// batch runs at a time, whatever it claims to do.
    /// </summary>
    public BatchRun? TryStart(IBatchOperation operation, JsonObject? overrides, out string? blocker)
    {
        JsonObject settings = MergeSettings(SettingsFor(operation.Id), overrides);
        BatchSession? session = Open(
            operation.DisplayName,
            operation.Id,
            settings,
            (context, work) => operation.RunAsync(context, work),
            out blocker);

        if (session == null)
        {
            return null;
        }

        var run = new BatchRun(operation.Id, session);
        _runs.Insert(0, run);
        while (_runs.Count > RecentRunCapacity)
        {
            _runs.RemoveAt(_runs.Count - 1);
        }

        return run;
    }

    /// <summary>
    /// Takes the same gate under the same conditions and holds it until <see cref="BatchSession.End"/>,
    /// for a caller that drives a batch across many calls rather than being one body. State is scoped
    /// to <paramref name="name"/>.
    /// </summary>
    public BatchSession? TryOpenSession(string name, out string? blocker) =>
        Open(name, name, new JsonObject(), body: null, out blocker);

    /// <summary>Ends the active session from the outside — the recovery path for a script that
    /// crashed or a client that disconnected holding the gate. Honours the reload latch, so a session
    /// that had already written still reloads.</summary>
    public void ForceEnd()
    {
        if (ActiveSession is not { } session)
        {
            return;
        }

        session.Cancel();
        session.End();
    }

    private BatchSession? Open(
        string name,
        string stateNamespace,
        JsonObject settings,
        Func<BatchContext, WorkContext, Task>? body,
        out string? blocker)
    {
        // Asked before anything is constructed: TryRun would refuse too, but only after ActiveSession
        // had already been overwritten with a session that never starts.
        blocker = Context.Operations.Blocker;
        if (blocker != null)
        {
            return null;
        }

        var session = new BatchSession(this, name, settings, State.For(stateNamespace));
        ActiveSession = session;

        WorkHandle? handle = Context.Operations.TryRun(
            name,
            async work =>
            {
                session.BindWork(work);
                try
                {
                    // No body means the caller drives: hold the gate until someone ends the session.
                    if (body != null)
                    {
                        await body(session.Context, work).ConfigureAwait(false);
                    }
                    else
                    {
                        await session.Completion.ConfigureAwait(false);
                    }
                }
                finally
                {
                    LogTimings(name, session);
                    session.End();
                    if (ReferenceEquals(ActiveSession, session))
                    {
                        ActiveSession = null;
                    }

                    // Outside the success path on purpose: the latch is set when a write lands, so a
                    // batch that wrote and then faulted still leaves the world needing a re-read.
                    if (session.ReloadReason is { } reason)
                    {
                        Context.RequestReload($"{name}: {reason}");
                    }
                }
            },
            out blocker,
            // WorldOperations would reload unconditionally; a batch decides for itself, through the latch.
            reloadAfter: false);

        if (handle == null)
        {
            ActiveSession = null;
            return null;
        }

        session.BindHandle(handle);
        return session;
    }

    /// <summary>Writes a finished run's phase breakdown to its own log and to the Godot output, where
    /// it outlives the bounded log ring a long run scrolls through. Faulted and cancelled runs report
    /// too — where a run got stuck is exactly what a breakdown is for.</summary>
    private static void LogTimings(string name, BatchSession session)
    {
        foreach (string line in session.Timings.Format($"{name} phase breakdown"))
        {
            session.Log(line);
            GD.Print($"[Batch] {line}");
            DiagnosticLog.Log(line);
        }
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
