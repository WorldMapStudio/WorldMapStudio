using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// The whole surface a batch operation gets. Deliberately a forwarding facade with no logic of its
/// own: everything here lives somewhere in <see cref="Editor"/> and is repeated only so an operation
/// does not have to walk the context to find it.
///
/// Every <c>Task</c>-returning member does its work off the main thread. The reporting members are
/// cheap and lock-guarded, so they are safe from either thread.
/// </summary>
public sealed class BatchContext
{
    private readonly BatchSystem _batch;
    private readonly BatchStatusHolder _status;
    private readonly BatchReloadLatch _reload;
    private readonly WorkContext _work;

    internal BatchContext(
        BatchSystem batch,
        JsonObject settings,
        BatchOperationState state,
        BatchStatusHolder status,
        BatchReloadLatch reload,
        WorkContext work)
    {
        _batch = batch;
        _status = status;
        _reload = reload;
        _work = work;
        Settings = settings;
        State = state;
    }

    /// <summary>The settings this run was started with: the persisted blob with any per-run overrides
    /// merged over the top, frozen when the run started. Not the operation instance's fields, which
    /// the window keeps editing while the run is in flight.</summary>
    public JsonObject Settings { get; }

    /// <summary>This operation's slice of <see cref="BatchState"/> — where a watermark, a format
    /// stamp, or anything else it needs between runs lives.</summary>
    public BatchOperationState State { get; }

    /// <summary>The whole editor — the escape hatch for anything the facade does not forward.</summary>
    public EditorContext Editor => _batch.Context;

    public string ProjectFolder => ProjectStore.ProjectFolder(Editor.Project.Name);

    public ChunkChangeLog ChunkChanges => Editor.ChunkChanges;

    /// <summary>Where an operation reports what it found wrong with the data it was asked to process
    /// — the same list the landscape builder reports into.</summary>
    public ProblemSystem Problems => Editor.Problems;

    public IReadOnlyList<Map> Maps => Editor.Maps.Maps;

    public Map? FindMap(MapId map) => Editor.Maps.Maps.FirstOrDefault(candidate => candidate.Id == map);

    /// <summary>A map's landscape settings — the chunk sizing and resolution an operation needs to
    /// build a <see cref="LandscapeGrid"/> of its own.</summary>
    public LandscapeSettings? LoadLandscapeSettings(MapId map) => Editor.Landscape.LoadSettingsFor(map);

    /// <summary>Builds any number of chunks with one scene scan. See
    /// <see cref="LandscapeSystem.BuildFromStorageAsync"/>.</summary>
    public Task<LandscapeBuildResult?> BuildChunksAsync(MapId map, IReadOnlyList<ChunkCoord> coords) =>
        Editor.Landscape.BuildFromStorageAsync(map, coords);

    /// <summary>Stored scene entities overlapping <paramref name="region"/> — placements, procedural
    /// models, anything beyond the landscape itself.</summary>
    public Task<IReadOnlyList<SceneEntity>> ScanSceneAsync(MapId map, Aabb region) =>
        Editor.Database.ScanSceneAsync(map, region);

    /// <summary>What is happening right now. Forwarded to the work handle too, so the Work Queue
    /// window and the batch window never disagree.</summary>
    public void Step(string step)
    {
        _status.Step(step);
        _work.Step(step);
    }

    /// <summary>How far along, 0..1. An operation that cannot say simply never calls this.</summary>
    public void Progress(float fraction) => _status.Progress(fraction);

    /// <summary>The headline summary of what this run did.</summary>
    public void Report(string message) => _status.Report(message);

    /// <summary>One line into the run's scrolling log, which is a bounded ring.</summary>
    public void Log(string line) => _status.Log(line);

    /// <summary>
    /// Says this run has written to the database behind the loaded world's back, so the world must be
    /// reloaded when it finishes. A latch, not a return value: call it the moment the first write
    /// lands, and a run that faults afterwards still reloads.
    /// </summary>
    public void RequireReload(string reason) => _reload.Require(reason);
}
