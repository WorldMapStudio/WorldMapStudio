using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Covers <see cref="SceneEntity.Clone"/> and <see cref="SceneClipboard"/>, which back copy/paste for
/// the object, procedural mesh and road tools alike (they all just select <see cref="SceneEntity"/>
/// through the shared <see cref="SelectionSystem"/>).
/// </summary>
public static class SceneClipboardTests
{
    [EditorTest(Category = "Clipboard", Thread = TestThread.Background)]
    public static void Clone_gets_a_fresh_identity_and_no_persisted_record()
    {
        var entity = new SceneEntity { Name = "Torch", RecordId = 7, ParentRecordId = 3 };
        entity.Transform = new Transform3D(Basis.Identity, new Vector3(1.0f, 2.0f, 3.0f));

        SceneEntity clone = entity.Clone();

        Assert.AreNotEqual(entity.Id, clone.Id);
        Assert.IsNull(clone.RecordId);
        Assert.IsNull(clone.ParentRecordId);
        Assert.AreEqual(entity.Name, clone.Name);
        Assert.IsTrue(clone.Transform.IsEqualApprox(entity.Transform));
    }

    [EditorTest(Category = "Clipboard", Thread = TestThread.Background)]
    public static void Clone_duplicates_component_data_independently()
    {
        var entity = new SceneEntity();
        entity.AddComponent(new MarkerComponent { Shape = MarkerShape.Cube });

        SceneEntity clone = entity.Clone();
        MarkerComponent? cloneMarker = clone.Component<MarkerComponent>();

        Assert.IsNotNull(cloneMarker);
        Assert.AreEqual(MarkerShape.Cube, cloneMarker!.Shape);
        Assert.IsFalse(ReferenceEquals(entity.Component<MarkerComponent>(), cloneMarker));
    }

    [EditorTest(Category = "Clipboard", Thread = TestThread.Background)]
    public static void Clone_gives_a_drawing_target_its_own_pixel_buffer()
    {
        var entity = new SceneEntity();
        var target = new DrawingTargetComponent();
        entity.AddComponent(target);
        target.Paint(Vector3.Zero, 100.0f, 1.0f, erase: false);

        SceneEntity clone = entity.Clone();
        var cloneTarget = clone.Component<DrawingTargetComponent>()!;
        byte[] clonedPixels = cloneTarget.CopyPixels();

        // Mutating the original after cloning must not leak into the clone's buffer.
        target.Paint(new Vector3(20.0f, 0.0f, 20.0f), 5.0f, 1.0f, erase: false);

        Assert.IsTrue(clonedPixels.SequenceEqual(cloneTarget.CopyPixels()),
            "the clone's pixel buffer must not be affected by edits to the original made after cloning");
        Assert.IsFalse(target.CopyPixels().SequenceEqual(clonedPixels), "sanity: the original actually changed");
    }

    [EditorTest(Category = "Clipboard", Thread = TestThread.Background)]
    public static void Copy_survives_the_source_entity_being_deleted_before_paste()
    {
        var scene = new SceneEntityRegistry();
        var entity = new SceneEntity { Name = "Crate" };
        entity.AddComponent(new MarkerComponent { Shape = MarkerShape.Cube });
        scene.Add(entity);

        var clipboard = new SceneClipboard();
        clipboard.Copy([entity]);

        // Simulate the source unloading (streaming) or being undone entirely.
        scene.Remove(entity);

        var pasted = clipboard.Paste(new MapId(0));

        Assert.AreEqual(1, pasted.Count);
        Assert.AreNotEqual(entity.Id, pasted[0].Id);
        Assert.AreEqual("Crate", pasted[0].Name);
        Assert.AreEqual(MarkerShape.Cube, pasted[0].Component<MarkerComponent>()!.Shape);
    }

    [EditorTest(Category = "Clipboard", Thread = TestThread.Background)]
    public static void Pasting_twice_never_shares_entities_or_components()
    {
        var entity = new SceneEntity();
        entity.AddComponent(new MarkerComponent());

        var clipboard = new SceneClipboard();
        clipboard.Copy([entity]);

        var first = clipboard.Paste(new MapId(0));
        var second = clipboard.Paste(new MapId(0));

        Assert.AreNotEqual(first[0].Id, second[0].Id);
        Assert.IsFalse(ReferenceEquals(first[0].Component<MarkerComponent>(), second[0].Component<MarkerComponent>()));
    }

    [EditorTest(Category = "Clipboard", Thread = TestThread.Background)]
    public static void Paste_places_entities_into_the_requested_map()
    {
        var entity = new SceneEntity { Map = new MapId(1) };
        var clipboard = new SceneClipboard();
        clipboard.Copy([entity]);

        var pasted = clipboard.Paste(new MapId(5));

        Assert.AreEqual(5, pasted[0].Map.Value);
    }

    [EditorTest(Category = "Clipboard", Thread = TestThread.Background)]
    public static void Copying_parent_and_child_together_preserves_the_link()
    {
        var parent = new SceneEntity { Name = "Parent" };
        var child = new SceneEntity { Name = "Child", Parent = parent };

        var clipboard = new SceneClipboard();
        clipboard.Copy([parent, child]);
        var pasted = clipboard.Paste(new MapId(0));

        SceneEntity pastedParent = pasted.Single(e => e.Name == "Parent");
        SceneEntity pastedChild = pasted.Single(e => e.Name == "Child");
        Assert.IsTrue(ReferenceEquals(pastedChild.Parent, pastedParent));
    }

    [EditorTest(Category = "Clipboard", Thread = TestThread.Background)]
    public static void Copying_only_the_child_drops_the_dangling_parent_link()
    {
        var parent = new SceneEntity { Name = "Parent" };
        var child = new SceneEntity { Name = "Child", Parent = parent };

        var clipboard = new SceneClipboard();
        clipboard.Copy([child]);
        var pasted = clipboard.Paste(new MapId(0));

        Assert.IsNull(pasted.Single().Parent);
    }

    [EditorTest(Category = "Clipboard", Thread = TestThread.Background)]
    public static void Batch_paste_command_reports_chunk_impacts_from_every_created_entity()
    {
        var scene = new SceneEntityRegistry();
        var a = new SceneEntity();
        var b = new SceneEntity();
        scene.Add(a);
        scene.Add(b);

        var commandA = new CreateEntityCommand(scene, a);
        var commandB = new CreateEntityCommand(scene, b);
        var batch = new BatchEditCommand("Paste 2 entities", [commandA, commandB]);

        var ranges = ChunkChangeRegistry.ReduceImpacts([batch], _ => true);

        Assert.AreEqual(2, ranges.Count, "each pasted entity's create should still dirty its chunk");
    }
}
