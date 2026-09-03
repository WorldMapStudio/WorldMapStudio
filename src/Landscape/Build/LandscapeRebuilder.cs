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

    // Registry membership and catalog content are compared as a pair rather than folded into one
    // hash: a collision here, or a real hash landing on the "nothing yet" sentinel, is terrain that
    // silently never rebuilds — the hardest possible bug to find, bought for nothing.
    private (int Registry, int Content) _catalogVersion = (-1, -1);
    private int _historyRevision = -1;
    private int _sceneVersion = -1;

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

        // Forget what the loaded set was, so the entities of the map being entered read as new.
        _sceneVersion = -1;
    }

    private void NoticeChanges(LandscapeSystem landscape)
    {
        // Settings can change the grid itself, so chunk identity is no longer comparable — start over
        // and let streaming re-scan rather than trying to patch what is loaded.
        if (_settingsVersion != landscape.Version)
        {
            _settingsVersion = landscape.Version;
            _catalogVersion = (_context.Catalog.Version, landscape.Catalog.ContentVersion);
            _historyRevision = _context.EditSessions.Active.History.Revision;
            _sceneVersion = _context.Scene.Version;
            Reset();
            _context.Streaming.Invalidate();
            return;
        }

        // A catalog edit — a material's height amount, a layer's draw order — can change any chunk
        // that binds it, and nothing cheap narrows that down. Membership and content both count: an
        // edit mutates a catalog entity in place, which the registry's version never sees.
        (int, int) catalog = (_context.Catalog.Version, landscape.Catalog.ContentVersion);
        if (_catalogVersion != catalog)
        {
            _catalogVersion = catalog;
            MarkAll();
        }

        // Two things change the deformer set. An edit is the obvious one. The other is streaming:
        // a chunk is built from a snapshot taken when its scan *started*, so entities that same scan
        // loads were not there yet. In steady flight the load margin covers it — a deformer enters
        // the margin a scan before its chunk enters view — but on a cold start nothing is loaded at
        // all, and the first chunks come out empty until something notices.
        bool edited = _historyRevision != _context.EditSessions.Active.History.Revision;
        bool loadedSetChanged = _sceneVersion != _context.Scene.Version;
        if (!edited && !loadedSetChanged)
        {
            return;
        }

        _historyRevision = _context.EditSessions.Active.History.Revision;
        _sceneVersion = _context.Scene.Version;

        // Rebuilding a chunk replaces its content in place and never touches the registry, so
        // reacting to the scene version cannot feed itself.
        List<ILandscapeDeformer> deformers = _context.Scene.Entities
            .SelectMany(entity => entity.Components)
            .OfType<ILandscapeDeformer>()
            .ToList();
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

        // The halo matters here as much as in the builder (see LandscapeBuilder.SampleRadius): a chunk
        // whose own channels are untouched can still read a changed neighbour's across the shared edge,
        // so it is stale too — even when nothing declares extra sample radius, which is why this is
        // floored to one chunk rather than trusting the catalog's possibly-zero raw value.
        float radius = Mathf.Max(grid.ChunkSize, landscape.Catalog.MaxSampleRadius);
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

            // Still on the worker: each chunk's mesh and material is real Godot resource construction
            // (texture decode, mesh upload), and building it here means the main-thread slice below
            // only has to attach it to a node, not build it.
            ctx.Step("Building visuals");
            var visuals = new Dictionary<ChunkCoord, (ArrayMesh Mesh, ShaderMaterial Material)>();
            foreach (KeyValuePair<ChunkCoord, LandscapeChunkOutput> built in result.Chunks)
            {
                visuals[built.Key] = (
                    LandscapeChunkMesh.BuildMesh(built.Value, grid.ChunkSize),
                    LandscapeChunkMesh.BuildMaterial(built.Value, _context.Assets, snapshot.Settings));
            }

            await ctx.SwitchToMain();
            ctx.Step("Applying");
            await ApplyAsync(ctx, result, visuals);

            landscape.Reporter.Report(result, grid, snapshot.Deformers);
        });
    }

    // Main thread. Attaching an already-built mesh and material is cheap, but this still yields
    // between small batches so a very large rebuild's node churn never fills a whole frame budget.
    private async System.Threading.Tasks.Task ApplyAsync(
        WorkContext ctx,
        LandscapeBuildResult result,
        IReadOnlyDictionary<ChunkCoord, (ArrayMesh Mesh, ShaderMaterial Material)> visuals)
    {
        // Built once rather than re-scanned per applied chunk: a coord's chunk may no longer resolve
        // if streaming unloaded it while this was building, but that check does not need an O(loaded
        // chunk count) scan for every one of the (possibly hundreds of) chunks being applied here —
        // that made a large rebuild at a real view distance quadratic in the chunk count.
        var loaded = new Dictionary<ChunkCoord, LandscapeChunk>();
        foreach (LandscapeChunk candidate in _context.Scene.Entities.OfType<LandscapeChunk>())
        {
            loaded[candidate.Coord] = candidate;
        }

        int applied = 0;
        foreach (KeyValuePair<ChunkCoord, LandscapeChunkOutput> built in result.Chunks)
        {
            loaded.TryGetValue(built.Key, out LandscapeChunk? chunk);

            (ArrayMesh mesh, ShaderMaterial material) = visuals[built.Key];
            chunk?.Rebuild(built.Value, mesh, material);

            if (++applied % ApplyBatch == 0)
            {
                await ctx.Yield();
            }
        }
    }
}
