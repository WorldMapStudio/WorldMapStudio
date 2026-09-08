using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

public static class ChunkChangeLogTests
{
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
        var entity = new SceneEntity();
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
        var entity = new SceneEntity();
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
        var entity = new SceneEntity();
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
        var entity = new SceneEntity();
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
        var entity = new SceneEntity();
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
        var placement = new SceneEntity { Transform = new Transform3D(Basis.Identity, new Vector3(200.0f, 0.0f, 200.0f)) };
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
