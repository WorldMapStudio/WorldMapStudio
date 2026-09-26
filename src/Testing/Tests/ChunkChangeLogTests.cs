using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Microsoft.Data.Sqlite;

namespace WorldMapStudio;

public static class ChunkChangeLogTests
{
    private sealed class Scratch : IDisposable
    {
        private readonly string _databasePath;

        private Scratch(EditorContext context, string databasePath)
        {
            Context = context;
            _databasePath = databasePath;
        }

        public EditorContext Context { get; }

        public EditorStorage Storage => Context.Database.EditorStorage;

        public static Scratch Open()
        {
            string databasePath = Path.Combine(Path.GetTempPath(), $"__wms_chunk_change_reconcile_test_{Guid.NewGuid():N}.doltlite");
            var context = new EditorContext(new Node3D(), new Project { Name = "__wms_chunk_change_reconcile_test__" });
            context.Database.EditorStorage.Location.DatabasePath = databasePath;
            context.Database.EditorStorage.EnsureSchema();
            return new Scratch(context, databasePath);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
    }

    private static LandscapeGrid TestGrid() => new(new LandscapeSettings());

    /// <summary>
    /// Exercises <see cref="EditorStorage.ReconcileChunkChangesAsync"/> end to end against a real
    /// database across two maps at once: a chunk something still occupies is upserted, and a chunk the
    /// commit touched but nothing occupies any more is removed — the same outcome the old per-map,
    /// per-step <c>ChunkChangeLog.RecordCommit</c> produced, now from one reconcile call.
    /// </summary>
    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static async Task A_reconcile_upserts_occupied_chunks_and_removes_vacated_ones_across_two_maps()
    {
        using Scratch scratch = Scratch.Open();
        EditorStorage storage = scratch.Storage;
        LandscapeGrid grid = TestGrid();

        var mapA = new MapId(1);
        var mapB = new MapId(2);
        var occupied = new ChunkCoord(0, 0);
        var stale = new ChunkCoord(5, 5);

        var entityA = new MapSceneEntity { Map = mapA, Transform = new Transform3D(Basis.Identity, grid.BoundsOf(occupied).GetCenter()) };
        entityA.AddComponent(new MarkerComponent());
        var entityB = new MapSceneEntity { Map = mapB, Transform = new Transform3D(Basis.Identity, grid.BoundsOf(occupied).GetCenter()) };
        entityB.AddComponent(new MarkerComponent());
        await storage.CommitAsync([entityA, entityB], []).ConfigureAwait(false);

        // Stamped ahead of time so the reconcile's removal of a chunk nothing occupies is observable.
        await storage.UpsertChunkChangesAsync([(mapA.Value, stale.X, stale.Y), (mapB.Value, stale.X, stale.Y)]).ConfigureAwait(false);

        Aabb region = grid.BoundsOf(occupied).Merge(grid.BoundsOf(stale));
        var plan = new ChunkChangeReconcilePlan(
            TouchedByMap: new Dictionary<MapId, (IReadOnlyCollection<ChunkCoord> Chunks, Aabb Region)>
            {
                [mapA] = ([occupied, stale], region),
                [mapB] = ([occupied, stale], region),
            },
            Grids: new Dictionary<MapId, LandscapeGrid?> { [mapA] = grid, [mapB] = grid },
            LoadedEntityIds: [],
            EditedSharedResources: [],
            CatalogChangedMaps: []);

        await storage.ReconcileChunkChangesAsync(plan).ConfigureAwait(false);

        IReadOnlyList<ChunkChange> rowsA = await storage.LoadChangedSinceAsync(DateTime.MinValue, mapA.Value).ConfigureAwait(false);
        IReadOnlyList<ChunkChange> rowsB = await storage.LoadChangedSinceAsync(DateTime.MinValue, mapB.Value).ConfigureAwait(false);

        Assert.AreEqual(1, rowsA.Count, "map A keeps only the occupied chunk");
        Assert.AreEqual(occupied, rowsA[0].Coord);
        Assert.AreEqual(1, rowsB.Count, "map B keeps only the occupied chunk");
        Assert.AreEqual(occupied, rowsB[0].Coord);
    }

    /// <summary>
    /// A shared resource's stored, not-currently-loaded placement is stamped from its persisted bounds,
    /// and a catalog-changed map's whole chunk set is restamped — in the same reconcile call that also
    /// handles the touched-chunk occupancy split above.
    /// </summary>
    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static async Task A_reconcile_stamps_unloaded_resource_placements_and_touches_catalog_changed_maps()
    {
        using Scratch scratch = Scratch.Open();
        EditorStorage storage = scratch.Storage;
        LandscapeGrid grid = TestGrid();

        var map = new MapId(1);
        var otherMap = new MapId(2);
        var placementChunk = new ChunkCoord(2, 2);

        var placement = new MapSceneEntity { Map = map, Transform = new Transform3D(Basis.Identity, grid.BoundsOf(placementChunk).GetCenter()) };
        placement.AddComponent(new MarkerComponent());
        await storage.CommitAsync([placement], []).ConfigureAwait(false);

        // A procedural-mesh reference row on that same entity, inserted directly rather than through a
        // built ProceduralComponent — this test is about the reconcile's storage query, not the
        // procedural build pipeline.
        await using (EditorDbContext write = storage.CreateContext())
        {
            write.Set<SceneProceduralComponentRecord>().Add(new SceneProceduralComponentRecord
            {
                EntityId = placement.RecordId!.Value,
                ModelId = 42,
            });
            await write.SaveChangesAsync().ConfigureAwait(false);
        }

        await storage.UpsertChunkChangesAsync([(otherMap.Value, 1, 1)]).ConfigureAwait(false);
        DateTime before = (await storage.LoadChangedSinceAsync(DateTime.MinValue, otherMap.Value).ConfigureAwait(false)).Single().LastEditedUtc;

        var plan = new ChunkChangeReconcilePlan(
            TouchedByMap: new Dictionary<MapId, (IReadOnlyCollection<ChunkCoord> Chunks, Aabb Region)>(),
            Grids: new Dictionary<MapId, LandscapeGrid?> { [map] = grid },
            LoadedEntityIds: [], // the placement is not currently loaded
            EditedSharedResources: [(typeof(ProceduralModel), 42)],
            CatalogChangedMaps: [otherMap]);

        await storage.ReconcileChunkChangesAsync(plan).ConfigureAwait(false);

        IReadOnlyList<ChunkChange> stamped = await storage.LoadChangedSinceAsync(DateTime.MinValue, map.Value).ConfigureAwait(false);
        Assert.AreEqual(1, stamped.Count, "the unloaded placement's chunk is stamped from its stored bounds");
        Assert.AreEqual(placementChunk, stamped[0].Coord);

        ChunkChange touched = (await storage.LoadChangedSinceAsync(DateTime.MinValue, otherMap.Value).ConfigureAwait(false)).Single();
        Assert.IsTrue(touched.LastEditedUtc > before, "the catalog-changed map's existing chunk is restamped");
    }

    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static void Chunk_range_enumerates_every_coordinate_in_the_rectangle()
    {
        var range = new ChunkRange(new MapId(1), new ChunkCoord(-1, 2), new ChunkCoord(1, 3));

        var coords = range.Coords().ToList();

        Assert.AreEqual(6, coords.Count);
        Assert.IsTrue(coords.Contains(new ChunkCoord(-1, 2)));
        Assert.IsTrue(coords.Contains(new ChunkCoord(1, 3)));
    }

    /// <summary>
    /// The rule every consumer's cache depends on: a run stores the newest LastEditedUtc it actually
    /// observed, never the wall clock. A commit landing while the run is in flight is stamped after
    /// the rows the run saw, so a clock-based watermark would put it on the already-done side and skip
    /// it forever; taking the watermark from the data makes the worst case a redundant reprocess.
    /// </summary>
    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static void A_chunk_edited_during_a_run_is_picked_up_by_the_next_run()
    {
        var map = new MapId(1);
        DateTime start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        List<ChunkChange> observed =
        [
            new(map, new ChunkCoord(0, 0), start),
            new(map, new ChunkCoord(1, 0), start.AddSeconds(1)),
        ];

        // Lands after the query returned, while the run is still writing.
        var duringRun = new ChunkChange(map, new ChunkCoord(2, 0), start.AddSeconds(2));
        DateTime runFinished = start.AddSeconds(30);

        DateTime watermark = observed.Max(change => change.LastEditedUtc);

        Assert.IsTrue(duringRun.LastEditedUtc > watermark, "the mid-run edit must remain unprocessed");
        Assert.IsTrue(duringRun.LastEditedUtc < runFinished, "and a wall-clock watermark would have skipped it");
    }

    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static void Undone_edit_does_not_report_a_committed_chunk_change()
    {
        var entity = new MapSceneEntity();
        var history = new UndoHistory();
        Transform3D before = Transform3D.Identity;
        Transform3D after = new(Basis.Identity, new Vector3(64.0f, 0.0f, 0.0f));
        entity.Transform = after;
        history.Record(new TransformEntitiesCommand([entity], [before], [after]));
        history.Undo();

        var ranges = ChunkChangeLog.ReduceImpacts(history.UndoStack, _ => true);

        Assert.AreEqual(0, ranges.Count, "only applied commands should contribute at commit time");
    }

    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static void Moving_back_to_the_start_reports_no_chunk_change()
    {
        var entity = new MapSceneEntity();
        var history = new UndoHistory();
        Transform3D a = Transform3D.Identity;
        Transform3D b = new(Basis.Identity, new Vector3(64.0f, 0.0f, 0.0f));

        entity.Transform = b;
        history.Record(new TransformEntitiesCommand([entity], [a], [b]));
        entity.Transform = a;
        history.Record(new TransformEntitiesCommand([entity], [b], [a]));

        var ranges = ChunkChangeLog.ReduceImpacts(history.UndoStack, _ => true);

        Assert.AreEqual(0, ranges.Count, "the final committed state matches the starting state");
    }

    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static void Moving_once_reports_the_original_and_final_spans()
    {
        var entity = new MapSceneEntity();
        Transform3D before = Transform3D.Identity;
        Transform3D after = new(Basis.Identity, new Vector3(64.0f, 0.0f, 0.0f));
        entity.Transform = after;

        var command = new TransformEntitiesCommand([entity], [before], [after]);
        var ranges = ChunkChangeLog.ReduceImpacts([command], _ => true);

        Assert.AreEqual(1, ranges.Count);
        // A componentless SceneEntity's default bounds are a unit box centered on its origin (see
        // SceneEntity.EffectiveLocalBounds), so at the identity transform the span starts at -0.5, not 0.
        Assert.AreEqual(-0.5f, ranges[0].Before!.Bounds.Position.X);
        Assert.IsTrue(ranges[0].After!.Bounds.Position.X > ranges[0].Before!.Bounds.Position.X);
    }

    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static void Uncommitted_entities_are_ignored_by_the_commit_reducer()
    {
        var entity = new MapSceneEntity();
        Transform3D before = Transform3D.Identity;
        Transform3D after = new(Basis.Identity, new Vector3(64.0f, 0.0f, 0.0f));
        entity.Transform = after;

        var command = new TransformEntitiesCommand([entity], [before], [after]);
        var ranges = ChunkChangeLog.ReduceImpacts([command], _ => false);

        Assert.AreEqual(0, ranges.Count);
    }

    /// <summary>
    /// A catalog edit reshapes chunks through a reference, not a bounds, so no snapshot reports it and
    /// the whole map has to be restamped instead. The map comes off the edited entity.
    /// </summary>
    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static void A_committed_landscape_catalog_edit_marks_its_map()
    {
        var material = new LandscapeMaterial { Map = new MapId(3) };
        var command = new SetFieldCommand<string>(material, "alpha parameters", _ => { }, "", "falloff=2");

        IReadOnlyList<MapId> maps = ChunkChangeLog.CatalogChangedMaps([command], _ => true);

        Assert.AreEqual(1, maps.Count);
        Assert.AreEqual(new MapId(3), maps[0]);
    }

    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static void An_uncommitted_catalog_edit_marks_no_map()
    {
        var layer = new LandscapeLayer { Map = new MapId(3) };
        var command = new SetFieldCommand<int>(layer, "draw order", _ => { }, 0, 1);

        Assert.AreEqual(0, ChunkChangeLog.CatalogChangedMaps([command], _ => false).Count);
    }

    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static void A_non_catalog_edit_marks_no_map()
    {
        var entity = new MapSceneEntity();
        var command = new TransformEntitiesCommand([entity], [Transform3D.Identity], [Transform3D.Identity]);

        Assert.AreEqual(0, ChunkChangeLog.CatalogChangedMaps([command], _ => true).Count);
    }

    /// <summary>
    /// A shared-resource edit fans its chunk impact out to placements that are never pinned — only the
    /// resource is — so the reducer must gate on the command's target, not each impact's entity, or
    /// the whole fan-out is dropped at commit (which is exactly the "no chunks update" bug).
    /// </summary>
    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static void A_shared_resource_edit_keeps_its_fan_out_when_the_resource_committed()
    {
        var model = new ProceduralModel { RecordId = 5 };
        var placement = new MapSceneEntity { Transform = new Transform3D(Basis.Identity, new Vector3(200.0f, 0.0f, 200.0f)) };
        ChunkChangeSnapshot before = ChunkChangeSnapshot.Capture(placement);
        placement.Transform = new Transform3D(Basis.Identity, new Vector3(600.0f, 0.0f, 200.0f));
        ChunkChangeSnapshot after = ChunkChangeSnapshot.Capture(placement);
        var command = new SharedResourceStub(model, new ChunkChangeImpact(placement, before, after));

        var kept = ChunkChangeLog.ReduceImpacts([command], entity => ReferenceEquals(entity, model));
        var dropped = ChunkChangeLog.ReduceImpacts([command], _ => false);

        Assert.AreEqual(1, kept.Count, "the model was committed, so the placement's impact stands");
        Assert.AreEqual(0, dropped.Count, "nothing committed, nothing stamped");
    }

    private sealed class SharedResourceStub(ProceduralModel model, ChunkChangeImpact impact)
        : IEditCommand, IChunkChangeCommand, ISharedResourceChunkCommand
    {
        public IReadOnlyList<IEntity> Targets { get; } = [model];

        public IReadOnlyList<ChunkChangeImpact> ChunkImpacts { get; } = [impact];

        public (Type Type, int Id)? SharedResource => (typeof(ProceduralModel), model.RecordId!.Value);

        public string Description => "stub";

        public void Apply()
        {
        }

        public void Revert()
        {
        }
    }
}
