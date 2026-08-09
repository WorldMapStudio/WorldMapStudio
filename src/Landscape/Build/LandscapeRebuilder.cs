using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Keeps loaded chunks in step with the entities that shape them, rebuilding only what actually went
/// stale.
///
/// Terrain is derived, so nothing edits it directly: every trigger here is an edit to something else.
/// The edit history's revision is the signal for entity changes — create, delete, gizmo drag,
/// inspector field, undo, redo all land as commands — which means undo restores terrain through the
/// same path as any other edit, with no terrain-specific command types anywhere.
///
/// Work runs on the <see cref="WorkQueue"/>: building on a worker, applying on the main thread in
/// small slices so a large rebuild never stalls a frame. Chunks are ordered by distance from the
/// camera, so what the user is looking at lands first.
/// </summary>
public sealed class LandscapeRebuilder
{
    /// <summary>Chunks applied per main-thread slice. Each one rebuilds a mesh, so keep it small.</summary>
    private const int ApplyBatch = 4;

    private readonly EditorContext _context;
    private readonly LandscapeDirtyTracker _tracker = new();
    private readonly HashSet<ChunkCoord> _dirty = [];

    private WorkHandle? _running;
    private int _settingsVersion = -1;
    private int _catalogVersion = -1;
    private int _historyRevision = -1;
    private bool _primed;

    public LandscapeRebuilder(EditorContext context)
    {
        _context = context;
    }

    /// <summary>Chunks waiting to be rebuilt, for the debug window.</summary>
    public int PendingChunks => _dirty.Count;

    public bool IsBuilding => _running is { State: WorkState.Queued or WorkState.Executing };

    /// <summary>Called every frame on the main thread with the viewport focus.</summary>
    public void Update(Vector3 focus)
    {
        LandscapeSystem landscape = _context.Landscape;
        if (!landscape.IsEnabled)
        {
            Reset();
            return;
        }

        NoticeChanges(landscape);

        if (_dirty.Count > 0 && !IsBuilding)
        {
            Schedule(landscape, focus);
        }
    }

    /// <summary>Forgets everything, e.g. when the map changes and the loaded chunks are replaced.</summary>
    public void Reset()
    {
        _dirty.Clear();
        _tracker.Clear();
        _primed = false;
    }

    private void NoticeChanges(LandscapeSystem landscape)
    {
        // Settings can change the grid itself, so chunk identity is no longer comparable — start over
        // and let streaming re-scan rather than trying to patch what is loaded.
        if (_settingsVersion != landscape.Version)
        {
            _settingsVersion = landscape.Version;
            _catalogVersion = _context.Catalog.Version;
            _historyRevision = _context.EditSessions.Active.History.Revision;
            Reset();
            _context.Streaming.Invalidate();
            return;
        }

        List<ILandscapeDeformer> deformers = _context.Scene.Entities.OfType<ILandscapeDeformer>().ToList();

        if (!_primed)
        {
            // Streaming has just built these chunks from exactly this state; recording it without
            // reporting anything stops the first frame queueing a pointless rebuild of everything.
            _primed = true;
            _tracker.Prime(deformers);
            _catalogVersion = _context.Catalog.Version;
            _historyRevision = _context.EditSessions.Active.History.Revision;
            return;
        }

        // A catalog edit — a material's height amount, a layer's draw order — can change any chunk
        // that binds it, and nothing cheap narrows that down.
        if (_catalogVersion != _context.Catalog.Version)
        {
            _catalogVersion = _context.Catalog.Version;
            MarkAll();
        }

        if (_historyRevision == _context.EditSessions.Active.History.Revision)
        {
            return;
        }

        _historyRevision = _context.EditSessions.Active.History.Revision;
        foreach (Aabb region in _tracker.Collect(deformers))
        {
            MarkRegion(landscape, region);
        }
    }

    private void MarkAll()
    {
        foreach (LandscapeChunk chunk in _context.Scene.Entities.OfType<LandscapeChunk>())
        {
            _dirty.Add(chunk.Coord);
        }
    }

    private void MarkRegion(LandscapeSystem landscape, Aabb region)
    {
        if (landscape.Grid is not { } grid)
        {
            return;
        }

        // The halo matters here as much as in the builder: a chunk whose own channels are untouched
        // can still read a changed neighbour's, so it is stale too.
        float radius = landscape.Catalog.MaxSampleRadius;
        foreach (ChunkCoord coord in grid.OverlappingWithHalo(region, radius))
        {
            _dirty.Add(coord);
        }
    }

    private void Schedule(LandscapeSystem landscape, Vector3 focus)
    {
        if (landscape.TakeSnapshot() is not { } snapshot)
        {
            return;
        }

        // Only chunks that are actually loaded: anything else will be built by streaming when it
        // comes into range, using the same inputs.
        var loaded = new HashSet<ChunkCoord>(
            _context.Scene.Entities.OfType<LandscapeChunk>().Select(chunk => chunk.Coord));

        var grid = new LandscapeGrid(snapshot.Settings);
        List<ChunkCoord> coords = _dirty
            .Where(loaded.Contains)
            .OrderBy(coord => grid.OriginOf(coord).DistanceSquaredTo(focus))
            .ToList();

        _dirty.Clear();
        if (coords.Count == 0)
        {
            return;
        }

        _running = WorkQueue.Schedule($"Rebuild {coords.Count} chunks", async ctx =>
        {
            ctx.Step("Building");
            var builder = new LandscapeBuilder(snapshot.Settings, snapshot.Catalog, snapshot.Functions);
            LandscapeBuildResult result = builder.Build(coords, snapshot.Deformers);

            await ctx.SwitchToMain();
            ctx.Step("Applying");
            await ApplyAsync(ctx, result);

            landscape.Reporter.Report(result, grid, snapshot.Deformers);
        });
    }

    // Main thread. Each chunk rebuilds a mesh and a material, so this yields between small batches
    // rather than doing a whole block inside one frame's pump budget.
    private async System.Threading.Tasks.Task ApplyAsync(WorkContext ctx, LandscapeBuildResult result)
    {
        int applied = 0;
        foreach (KeyValuePair<ChunkCoord, LandscapeChunkOutput> built in result.Chunks)
        {
            // Re-resolved each slice: streaming may have unloaded a chunk while this was building.
            LandscapeChunk? chunk = _context.Scene.Entities
                .OfType<LandscapeChunk>()
                .FirstOrDefault(candidate => candidate.Coord == built.Key);

            chunk?.Rebuild(built.Value);

            if (++applied % ApplyBatch == 0)
            {
                await ctx.Yield();
            }
        }
    }
}
