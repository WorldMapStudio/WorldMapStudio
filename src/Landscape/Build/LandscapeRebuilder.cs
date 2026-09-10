using System.Collections.Generic;
using System.Diagnostics;
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

    // Last completed rebuild wave, for the debug window: a regression in wave size or duration is
    // visible here without pulling a trace.
    private int _lastWaveChunks;
    private double _lastWaveMs;

    // A paint stroke bumps an image's content every dab; rather than re-scanning every deformer each
    // of those frames, the paint path just sets this and the deformer collect runs on a timer while
    // the stroke is live. The stroke's recorded command still forces a full-fidelity collect on
    // mouse-up through the ordinary history-revision path.
    private bool _paintPending;
    private ulong _lastPaintCollectMs;
    private const double PaintCollectIntervalMs = 200.0;

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

    /// <summary>Chunk count of the last completed rebuild wave, for the debug window.</summary>
    public int LastWaveChunks => _lastWaveChunks;

    /// <summary>Wall-clock milliseconds the last completed rebuild wave took, for the debug window.</summary>
    public double LastWaveMs => _lastWaveMs;

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

        // Mid-stroke: fold the accumulated dabs into dirty regions rather than once per frame. The
        // moment a wave is not in flight is the useful moment, because the collect below is what the
        // schedule below it will pick up — waiting out a timer there only adds latency to terrain the
        // rebuilder is already idle and ready to redo. The interval is the cap for the other case, a
        // wave running long enough that a stroke would otherwise accumulate one enormous dirty set.
        // The full collect on mouse-up (via the recorded command) is what makes it exact.
        if (_paintPending)
        {
            ulong now = Time.GetTicksMsec();
            if (!IsBuilding || now - _lastPaintCollectMs >= PaintCollectIntervalMs)
            {
                _lastPaintCollectMs = now;
                _paintPending = false;
                CollectDeformerChanges(landscape);
            }
        }

        if (_dirty.Count > 0 && !IsBuilding)
        {
            Schedule(landscape, focus);
        }
    }

    /// <summary>Signals that a paint stroke changed an image this frame. Cheaper than bumping the
    /// scene version: the deformer collect it drives runs on a timer, not every frame.</summary>
    public void NoticePaint() => _paintPending = true;

    /// <summary>Marks the loaded chunks overlapping a world region stale, for a source that is not an
    /// entity edit and so moves no deformer's <see cref="ILandscapeDeformer.ContentVersion"/> — an
    /// image chunk streaming into or out of residency changes what the terrain under a placement was
    /// built from without touching the placement itself. Picked up on the next <see cref="Update"/>
    /// like any other dirty region.</summary>
    public void MarkDirty(Aabb worldRegion)
    {
        LandscapeSystem landscape = _context.Landscape;
        if (landscape.IsEnabled)
        {
            MarkRegion(landscape, worldRegion);
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

        // Two things change the deformer set. An edit is the obvious one. The other is streaming:
        // a chunk is built from a snapshot taken when its scan *started*, so entities that same scan
        // loads were not there yet. In steady flight the load margin covers it — a deformer enters
        // the margin a scan before its chunk enters view — but on a cold start nothing is loaded at
        // all, and the first chunks come out empty until something notices.
        bool edited = _historyRevision != _context.EditSessions.Active.History.Revision;
        bool loadedSetChanged = _sceneVersion != _context.Scene.Version;

        // A catalog edit — a material's height amount, a layer's draw order — can change any chunk
        // that binds it, and nothing cheap narrows that down. Both membership and in-place content
        // count, and both only move through a recorded command, so the catalog content hash (a walk
        // over every channel, layer and material) is only worth recomputing on a frame where an edit
        // actually landed rather than every frame.
        if (edited)
        {
            (int, int) catalog = (_context.Catalog.Version, landscape.Catalog.ContentVersion);
            if (_catalogVersion != catalog)
            {
                _catalogVersion = catalog;
                MarkAll();
            }
        }

        if (!edited && !loadedSetChanged)
        {
            return;
        }

        _historyRevision = _context.EditSessions.Active.History.Revision;
        _sceneVersion = _context.Scene.Version;
        _paintPending = false;

        CollectDeformerChanges(landscape);
    }

    // Turns every moved or content-changed deformer into the world regions that went stale. Rebuilding
    // a chunk replaces its content in place and never touches the registry, so reacting to the scene
    // version here cannot feed itself.
    private void CollectDeformerChanges(LandscapeSystem landscape)
    {
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
        foreach (LandscapeTerrainBatch batch in _context.Scene.Entities.OfType<LandscapeTerrainBatch>())
        {
            foreach (ChunkCoord coord in batch.Chunks.Keys)
            {
                _dirty.Add(coord);
            }
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

        int batchChunks = Mathf.Clamp(_context.View.TerrainBatchChunks, 1, LandscapeTerrainBatch.MaxTerrainBatchChunks);

        // Only batches that are actually loaded: anything else is streaming's job when it comes into
        // range, from the same inputs. A dirty chunk drags its whole batch in — the batch is one mesh.
        var loadedBatches = new Dictionary<LandscapeBatchCoord, LandscapeTerrainBatch>();
        foreach (LandscapeTerrainBatch batch in _context.Scene.Entities.OfType<LandscapeTerrainBatch>())
        {
            loadedBatches[batch.BatchCoord] = batch;
        }

        var grid = new LandscapeGrid(snapshot.Settings);

        var dirtyBatches = new HashSet<LandscapeBatchCoord>();
        foreach (ChunkCoord coord in _dirty)
        {
            LandscapeBatchCoord batchCoord = LandscapeBatchCoord.Of(coord, batchChunks);
            if (loadedBatches.ContainsKey(batchCoord))
            {
                dirtyBatches.Add(batchCoord);
            }
        }

        _dirty.Clear();
        if (dirtyBatches.Count == 0)
        {
            return;
        }

        List<LandscapeBatchCoord> ordered = dirtyBatches
            .OrderBy(batchCoord => grid.OriginOf(batchCoord.Origin(batchChunks)).DistanceSquaredTo(focus))
            .ToList();

        var coordsByBatch = new Dictionary<LandscapeBatchCoord, List<ChunkCoord>>();
        var union = new List<ChunkCoord>();
        foreach (LandscapeBatchCoord batchCoord in ordered)
        {
            List<ChunkCoord> coords = ExpandBatch(grid, batchCoord, batchChunks);
            coordsByBatch[batchCoord] = coords;
            union.AddRange(coords);
        }

        int waveChunks = union.Count;
        _running = WorkQueue.Schedule($"Rebuild {ordered.Count} terrain batches", async ctx =>
        {
            var stopwatch = Stopwatch.StartNew();
            ctx.Step("Building");
            var builder = new LandscapeBuilder(snapshot.Settings, snapshot.Catalog, snapshot.Functions);
            LandscapeBuildResult result = builder.Build(union, snapshot.Deformers);

            // Still on the worker: each batch's mesh and material is real Godot resource construction,
            // so the main-thread slice below only attaches it.
            ctx.Step("Building visuals");
            var visuals = new List<(LandscapeBatchCoord Coord, IReadOnlyDictionary<ChunkCoord, LandscapeChunkOutput> Chunks, ArrayMesh Mesh, ShaderMaterial Material)>();
            foreach (LandscapeBatchCoord batchCoord in ordered)
            {
                var outputs = new Dictionary<ChunkCoord, LandscapeChunkOutput>();
                foreach (ChunkCoord coord in coordsByBatch[batchCoord])
                {
                    if (result.Chunks.TryGetValue(coord, out LandscapeChunkOutput? output))
                    {
                        outputs[coord] = output;
                    }
                }

                if (outputs.Count == 0)
                {
                    continue;
                }

                var list = outputs
                    .OrderBy(pair => pair.Key.Y)
                    .ThenBy(pair => pair.Key.X)
                    .Select(pair => (pair.Key, pair.Value))
                    .ToList();

                visuals.Add((
                    batchCoord,
                    outputs,
                    LandscapeBatchMesh.BuildMesh(list, batchCoord, grid, batchChunks),
                    LandscapeBatchMesh.BuildMaterial(list, batchCoord, batchChunks, _context.Assets, snapshot.Settings)));
            }

            await ctx.SwitchToMain();
            ctx.Step("Applying");
            await ApplyAsync(ctx, visuals);

            landscape.Reporter.Report(result, grid, snapshot.Deformers);

            _lastWaveChunks = waveChunks;
            _lastWaveMs = stopwatch.Elapsed.TotalMilliseconds;
        });
    }

    private static List<ChunkCoord> ExpandBatch(LandscapeGrid grid, LandscapeBatchCoord batchCoord, int batchChunks)
    {
        ChunkCoord origin = batchCoord.Origin(batchChunks);
        var coords = new List<ChunkCoord>();
        for (int y = 0; y < batchChunks; y++)
        {
            for (int x = 0; x < batchChunks; x++)
            {
                var coord = new ChunkCoord(origin.X + x, origin.Y + y);
                if (grid.IsInLimits(coord))
                {
                    coords.Add(coord);
                }
            }
        }

        return coords;
    }

    // Main thread. Swapping an already-built mesh and material is cheap, but this still yields between
    // small batches so a very large rebuild's node churn never fills a whole frame budget.
    private async System.Threading.Tasks.Task ApplyAsync(
        WorkContext ctx,
        IReadOnlyList<(LandscapeBatchCoord Coord, IReadOnlyDictionary<ChunkCoord, LandscapeChunkOutput> Chunks, ArrayMesh Mesh, ShaderMaterial Material)> visuals)
    {
        var loaded = new Dictionary<LandscapeBatchCoord, LandscapeTerrainBatch>();
        foreach (LandscapeTerrainBatch candidate in _context.Scene.Entities.OfType<LandscapeTerrainBatch>())
        {
            loaded[candidate.BatchCoord] = candidate;
        }

        int applied = 0;
        foreach ((LandscapeBatchCoord coord, IReadOnlyDictionary<ChunkCoord, LandscapeChunkOutput> chunks, ArrayMesh mesh, ShaderMaterial material) in visuals)
        {
            if (loaded.TryGetValue(coord, out LandscapeTerrainBatch? batch))
            {
                batch.Rebuild(chunks, mesh, material);
            }
            else
            {
                // Streaming unloaded it while this built; free the orphaned resources.
                mesh.Dispose();
                LandscapeTerrainBatch.DisposeMaterial(material);
            }

            if (++applied % ApplyBatch == 0)
            {
                await ctx.Yield();
            }
        }
    }
}
