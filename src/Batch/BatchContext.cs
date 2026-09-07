using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// The whole surface a batch gets. Deliberately a forwarding facade with no logic of its own:
/// everything here lives somewhere in <see cref="Editor"/> and is repeated only so a batch does not
/// have to walk the context to find it.
///
/// Every <c>Task</c>-returning member does its work off the caller's thread, because a script's
/// session call arrives on the main thread and doing the work inline there would freeze rendering for
/// its duration. The reporting members are cheap and lock-guarded, so they are safe from either
/// thread.
/// </summary>
public sealed class BatchContext
{
    private readonly BatchSession _session;

    internal BatchContext(BatchSession session, JsonObject settings, BatchOperationState state)
    {
        _session = session;
        Settings = settings;
        State = state;
    }

    /// <summary>The settings this batch was started with: the persisted blob with any per-run
    /// overrides merged over the top, frozen when it started. Not the operation instance's fields,
    /// which the window keeps editing while a run is in flight.</summary>
    public JsonObject Settings { get; }

    /// <summary>This batch's slice of <see cref="BatchState"/> — where a watermark, a format stamp, or
    /// anything else it needs between runs lives.</summary>
    public BatchOperationState State { get; }

    /// <summary>The whole editor — the escape hatch for anything the facade does not forward.</summary>
    public EditorContext Editor => _session.Batch.Context;

    public string ProjectFolder => ProjectStore.ProjectFolder(Editor.Project.Name);

    public ChunkChangeLog ChunkChanges => Editor.ChunkChanges;

    /// <summary>Where a batch reports what it found wrong with the data it was asked to process — the
    /// same list the landscape builder reports into.</summary>
    public ProblemSystem Problems => Editor.Problems;

    public IReadOnlyList<Map> Maps => Editor.Maps.Maps;

    public Map? FindMap(MapId map) => Editor.Maps.Maps.FirstOrDefault(candidate => candidate.Id == map);

    /// <summary>A map's landscape settings — the chunk sizing and resolution a batch needs to build a
    /// <see cref="LandscapeGrid"/> of its own.</summary>
    public LandscapeSettings? LoadLandscapeSettings(MapId map) => Editor.Landscape.LoadSettingsFor(map);

    /// <summary>Builds any number of chunks with one scene scan. See
    /// <see cref="LandscapeSystem.BuildFromStorageAsync"/>.</summary>
    public Task<LandscapeBuildResult?> BuildChunksAsync(MapId map, IReadOnlyList<ChunkCoord> coords)
    {
        _session.Touch();
        return Task.Run(() => Editor.Landscape.BuildFromStorageAsync(map, coords));
    }

    /// <summary>Stored scene entities overlapping <paramref name="region"/> — placements, procedural
    /// models, anything beyond the landscape itself.</summary>
    public Task<IReadOnlyList<SceneEntity>> ScanSceneAsync(MapId map, Aabb region)
    {
        _session.Touch();
        return Task.Run(() => Editor.Database.ScanSceneAsync(map, region));
    }

    /// <summary>What is happening right now.</summary>
    public void Step(string step) => _session.Step(step);

    /// <summary>How far along, 0..1. A batch that cannot say simply never calls this.</summary>
    public void Progress(float fraction) => _session.Progress(fraction);

    /// <summary>The headline summary of what this batch did.</summary>
    public void Report(string message) => _session.Report(message);

    /// <summary>One line into the scrolling log, which is a bounded ring.</summary>
    public void Log(string line) => _session.Log(line);

    /// <summary>
    /// Says this batch has written to the database behind the loaded world's back, so the world must
    /// be reloaded when it finishes. A latch, not a return value: call it the moment the first write
    /// lands, and a batch that faults afterwards still reloads.
    /// </summary>
    public void RequireReload(string reason) => _session.RequireReload(reason);
}
