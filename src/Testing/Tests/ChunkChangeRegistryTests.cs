using System.Linq;
using Godot;

namespace WorldMapStudio;

public static class ChunkChangeRegistryTests
{
    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static void Undone_edit_does_not_report_a_committed_chunk_change()
    {
        var entity = new EmptyEntity();
        var history = new UndoHistory();
        Transform3D before = Transform3D.Identity;
        Transform3D after = new(Basis.Identity, new Vector3(64.0f, 0.0f, 0.0f));
        entity.Transform = after;
        history.Record(new TransformEntitiesCommand([entity], [before], [after]));
        history.Undo();

        var ranges = ChunkChangeRegistry.ReduceImpacts(history.UndoStack, _ => true);

        Assert.AreEqual(0, ranges.Count, "only applied commands should contribute at commit time");
    }

    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static void Moving_back_to_the_start_reports_no_chunk_change()
    {
        var entity = new EmptyEntity();
        var history = new UndoHistory();
        Transform3D a = Transform3D.Identity;
        Transform3D b = new(Basis.Identity, new Vector3(64.0f, 0.0f, 0.0f));

        entity.Transform = b;
        history.Record(new TransformEntitiesCommand([entity], [a], [b]));
        entity.Transform = a;
        history.Record(new TransformEntitiesCommand([entity], [b], [a]));

        var ranges = ChunkChangeRegistry.ReduceImpacts(history.UndoStack, _ => true);

        Assert.AreEqual(0, ranges.Count, "the final committed state matches the starting state");
    }

    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static void Moving_once_reports_the_original_and_final_spans()
    {
        var entity = new EmptyEntity();
        Transform3D before = Transform3D.Identity;
        Transform3D after = new(Basis.Identity, new Vector3(64.0f, 0.0f, 0.0f));
        entity.Transform = after;

        var command = new TransformEntitiesCommand([entity], [before], [after]);
        var ranges = ChunkChangeRegistry.ReduceImpacts([command], _ => true);

        Assert.AreEqual(1, ranges.Count);
        Assert.AreEqual(0.0f, ranges[0].Before!.Bounds.Position.X);
        Assert.IsTrue(ranges[0].After!.Bounds.Position.X > ranges[0].Before!.Bounds.Position.X);
    }

    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static void Uncommitted_entities_are_ignored_by_the_commit_reducer()
    {
        var entity = new EmptyEntity();
        Transform3D before = Transform3D.Identity;
        Transform3D after = new(Basis.Identity, new Vector3(64.0f, 0.0f, 0.0f));
        entity.Transform = after;

        var command = new TransformEntitiesCommand([entity], [before], [after]);
        var ranges = ChunkChangeRegistry.ReduceImpacts([command], _ => false);

        Assert.AreEqual(0, ranges.Count);
    }
}
