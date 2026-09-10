using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>What an operation is, for a caller choosing one.</summary>
public sealed class BatchOperationDescriptor
{
    internal BatchOperationDescriptor(IBatchOperation operation)
    {
        Id = operation.Id;
        Name = operation.DisplayName;
        Description = operation.Description;
    }

    [ScriptProperty]
    public string Id { get; }

    [ScriptProperty]
    public string Name { get; }

    [ScriptProperty]
    public string Description { get; }
}

/// <summary>A run's state at one instant, as JS sees it.</summary>
public sealed class BatchRunDescriptor
{
    internal BatchRunDescriptor(BatchRun run)
    {
        BatchStatus status = run.Status();
        WorkSnapshot work = run.Work?.Snapshot() ?? default;

        Id = run.Id;
        OperationId = run.OperationId;
        State = work.State.ToString();
        IsRunning = work.IsActive;
        Step = status.Step;
        Progress = status.Progress;
        Message = status.Message;
        Log = status.Log.ToArray();
        Timings = run.Timings().ToArray();
        ElapsedSeconds = work.ElapsedSeconds;
        Error = work.Error ?? "";
        ReloadRequired = run.ReloadRequired;
    }

    [ScriptProperty]
    public long Id { get; }

    [ScriptProperty]
    public string OperationId { get; }

    /// <summary>Queued, Executing, Completed, Faulted or Cancelled.</summary>
    [ScriptProperty]
    public string State { get; }

    [ScriptProperty]
    public bool IsRunning { get; }

    [ScriptProperty]
    public string Step { get; }

    /// <summary>0..1, or null when the operation cannot say how far along it is.</summary>
    [ScriptProperty]
    public float? Progress { get; }

    [ScriptProperty]
    public string Message { get; }

    [ScriptProperty]
    public string[] Log { get; }

    /// <summary>Where the run's wall clock went, slowest phase first. Populated while it is still
    /// running, so a long batch can be asked what it is stuck on.</summary>
    [ScriptProperty]
    public string[] Timings { get; }

    [ScriptProperty]
    public double ElapsedSeconds { get; }

    [ScriptProperty]
    public string Error { get; }

    [ScriptProperty]
    public bool ReloadRequired { get; }
}

/// <summary>
/// Batch operations and sessions, exposed to JS as <c>wms.batch</c>.
///
/// <see cref="Run"/> hands back a run id rather than a promise on purpose: the script host gives a
/// promise 60 seconds to settle, and a real batch outlives that, so awaiting one would report a
/// spurious failure while the run carried on. Poll <see cref="Status"/>, or use <see cref="Wait"/>
/// for short ones.
/// </summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class BatchScriptApi : IScriptModule
{
    /// <summary>Bounds one <see cref="Wait"/> call, not the batch. Under the host's own settlement
    /// deadline, which is where this ceiling comes from — it says nothing about how long a batch may
    /// take.</summary>
    private const int MaxWaitMilliseconds = 45_000;

    private const int PollMilliseconds = 50;

    private readonly BatchSystem _batch;

    private BatchSession? _session;
    private string? _sessionId;
    private long _nextSessionId = 1;

    public string Name => "batch";

    public float Priority => 0f;

    public BatchScriptApi(ScriptingSystem system)
    {
        _batch = system.Context.Batch;
        State = new BatchStateScriptApi(_batch);
    }

    /// <summary>Why a batch cannot start right now, or null when one can.</summary>
    [ScriptProperty]
    public string? Blocker => _batch.Context.Operations.Blocker;

    /// <summary>The open session's id, or null when nothing holds the gate.</summary>
    [ScriptProperty]
    public string? Session => _session is { IsOpen: true } ? _sessionId : null;

    /// <summary>Key/value storage shared with operations: <c>wms.batch.state.get(ns, key)</c>.</summary>
    [ScriptProperty]
    public BatchStateScriptApi State { get; }

    [ScriptFunction]
    public BatchOperationDescriptor[] List() =>
        _batch.Operations.Select(operation => new BatchOperationDescriptor(operation)).ToArray();

    /// <summary>An operation's persisted settings.</summary>
    [ScriptFunction]
    public IDictionary<string, object?> Settings(string id) => ScriptJson.ToScript(_batch.SettingsFor(id));

    /// <summary>Starts an operation and returns its run id. Never awaits — see the class remarks.
    /// <paramref name="overrides"/> is merged over the persisted settings for this run only.</summary>
    [ScriptFunction]
    public long Run(string id, object? overrides = null)
    {
        IBatchOperation operation = _batch.Find(id)
            ?? throw new InvalidOperationException($"No batch operation with id '{id}'.");

        BatchRun run = _batch.TryStart(operation, ScriptJson.ToJson(overrides), out string? blocker)
            ?? throw new InvalidOperationException(blocker ?? $"Could not start '{id}'.");

        return run.Id;
    }

    [ScriptFunction]
    public BatchRunDescriptor Status(long runId) => new(Require(runId));

    /// <summary>
    /// Resolves with the run's status once it finishes, or once <paramref name="timeoutMs"/> elapses —
    /// whichever comes first. The timeout bounds this call only: it never cancels anything, and the
    /// run carries on either way. Poll <see cref="Status"/> for anything longer.
    /// </summary>
    [ScriptFunction]
    public async Task<BatchRunDescriptor> Wait(long runId, int timeoutMs = MaxWaitMilliseconds)
    {
        BatchRun run = Require(runId);
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Clamp(timeoutMs, 0, MaxWaitMilliseconds));

        while (DateTime.UtcNow < deadline && run.Work?.Snapshot().IsActive == true)
        {
            await Task.Delay(PollMilliseconds).ConfigureAwait(false);
        }

        return new BatchRunDescriptor(run);
    }

    [ScriptFunction]
    public void Cancel(long runId) => Require(runId).Cancel();

    /// <summary>Takes the gate and holds it until <see cref="End"/>, so a caller can drive a batch
    /// across many calls. Throws with the blocker if the gate is not free.</summary>
    [ScriptFunction]
    public string Begin(string name)
    {
        BatchSession session = _batch.TryOpenSession(name, out string? blocker)
            ?? throw new InvalidOperationException(blocker ?? $"Could not open session '{name}'.");

        _session = session;
        _sessionId = $"{name}#{_nextSessionId++}";
        return _sessionId;
    }

    /// <summary>Releases the gate, reloading the world if anything in the session asked for it.</summary>
    [ScriptFunction]
    public void End(string sessionId) => Require(sessionId).End();

    /// <summary>Says the session has written behind the loaded world's back, so it must be re-read
    /// when the session ends. A latch — call it as soon as the first write lands.</summary>
    [ScriptFunction]
    public void RequireReload(string sessionId, string reason) => Require(sessionId).Context.RequireReload(reason);

    /// <summary>Writes into the same status a C# operation writes, so a script-driven session is as
    /// legible in the batch window as a native run. Accepts any of
    /// <c>{ step, progress, message, log }</c>.</summary>
    [ScriptFunction]
    public void Report(string sessionId, object? status)
    {
        BatchContext context = Require(sessionId).Context;
        if (ScriptJson.AsMap(status) is not { } fields)
        {
            return;
        }

        if (fields.TryGetValue("step", out object? step) && step != null)
        {
            context.Step(Convert.ToString(step, CultureInfo.InvariantCulture) ?? "");
        }

        if (fields.TryGetValue("progress", out object? progress) && progress != null)
        {
            context.Progress(Convert.ToSingle(progress, CultureInfo.InvariantCulture));
        }

        if (fields.TryGetValue("message", out object? message) && message != null)
        {
            context.Report(Convert.ToString(message, CultureInfo.InvariantCulture) ?? "");
        }

        if (fields.TryGetValue("log", out object? log) && log != null)
        {
            context.Log(Convert.ToString(log, CultureInfo.InvariantCulture) ?? "");
        }
    }

    /// <summary>
    /// Builds chunks and returns a summary each — coord, height range, layer materials, problems.
    /// Not the heightmaps themselves: pushing megabytes through the script host and out over HTTP is
    /// not the intended path, and anything that needs the arrays should be a C# operation.
    ///
    /// <paramref name="coords"/> is an array of <c>[x, y]</c> pairs. Runs on a worker, so the editor
    /// keeps drawing while it does.
    /// </summary>
    [ScriptFunction]
    public async Task<BatchChunkSummary[]> BuildChunks(string sessionId, int mapId, object? coords)
    {
        BatchContext context = Require(sessionId).Context;
        IReadOnlyList<ChunkCoord> requested = ScriptJson.ToCoords(coords);
        if (requested.Count == 0)
        {
            return [];
        }

        LandscapeBuildResult? result = await context.BuildChunksAsync(new MapId(mapId), requested).ConfigureAwait(false);
        if (result == null)
        {
            return [];
        }

        return result.Chunks.Values
            .Select(output => new BatchChunkSummary(output, result.Problems))
            .ToArray();
    }

    /// <summary>Stored scene entity ids overlapping a world-space box. Runs on a worker.</summary>
    [ScriptFunction]
    public async Task<long[]> ScanScene(
        string sessionId,
        int mapId,
        double minX, double minY, double minZ,
        double maxX, double maxY, double maxZ)
    {
        BatchContext context = Require(sessionId).Context;
        var region = new Godot.Aabb(
            new Godot.Vector3((float)minX, (float)minY, (float)minZ),
            new Godot.Vector3((float)(maxX - minX), (float)(maxY - minY), (float)(maxZ - minZ)));

        IReadOnlyList<SceneEntity> entities = await context.ScanSceneAsync(new MapId(mapId), region).ConfigureAwait(false);
        return entities.Select(entity => entity.Id.Value).ToArray();
    }

    private BatchRun Require(long runId) =>
        _batch.FindRun(runId) ?? throw new InvalidOperationException($"No batch run with id {runId}.");

    private BatchSession Require(string sessionId)
    {
        if (_session is not { IsOpen: true } session || _sessionId != sessionId)
        {
            throw new InvalidOperationException($"Session '{sessionId}' is not open.");
        }

        return session;
    }
}

/// <summary>What a built chunk looks like to a script — the shape of it, not its pixels.</summary>
public sealed class BatchChunkSummary
{
    internal BatchChunkSummary(LandscapeChunkOutput output, IReadOnlyList<(ChunkCoord Coord, LandscapeProblem Problem)> problems)
    {
        X = output.Coord.X;
        Y = output.Coord.Y;
        HeightResolution = output.HeightResolution;
        AlphaResolution = output.AlphaResolution;
        MinHeight = output.Heights.Length == 0 ? 0f : output.Heights.Min();
        MaxHeight = output.Heights.Length == 0 ? 0f : output.Heights.Max();
        Materials = output.Layers.Select(layer => layer.Material?.Name ?? "").ToArray();
        Problems = problems
            .Where(entry => entry.Coord.Equals(output.Coord))
            .Select(entry => entry.Problem.ToString() ?? "")
            .ToArray();
    }

    [ScriptProperty]
    public int X { get; }

    [ScriptProperty]
    public int Y { get; }

    [ScriptProperty]
    public int HeightResolution { get; }

    [ScriptProperty]
    public int AlphaResolution { get; }

    [ScriptProperty]
    public float MinHeight { get; }

    [ScriptProperty]
    public float MaxHeight { get; }

    /// <summary>Layer materials in compositing order; the first is the base when there is one.</summary>
    [ScriptProperty]
    public string[] Materials { get; }

    [ScriptProperty]
    public string[] Problems { get; }
}

/// <summary>
/// <see cref="BatchState"/> for scripts: <c>wms.batch.state.get(ns, key)</c>. Namespaces are prefixed
/// so a script can never write into an operation's own slice.
/// </summary>
public sealed class BatchStateScriptApi
{
    private const string Prefix = "script:";

    private readonly BatchSystem _batch;

    internal BatchStateScriptApi(BatchSystem batch)
    {
        _batch = batch;
    }

    [ScriptFunction]
    public Task<string?> Get(string ns, string key) => Task.Run(() => _batch.State.For(Prefix + ns).GetAsync(key));

    [ScriptFunction]
    public Task Set(string ns, string key, string value) => Task.Run(() => _batch.State.For(Prefix + ns).SetAsync(key, value));

    [ScriptFunction]
    public Task Remove(string ns, string key) => Task.Run(() => _batch.State.For(Prefix + ns).RemoveAsync(key));
}
