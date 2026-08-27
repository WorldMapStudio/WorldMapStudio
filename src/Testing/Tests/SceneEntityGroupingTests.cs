using System;
using System.Linq;
using Godot;

namespace WorldMapStudio;

public static class SceneEntityGroupingTests
{
    [EditorTest(Category = "Scene", Thread = TestThread.Background)]
    public static void Moving_a_parent_moves_nested_children()
    {
        var parent = new SceneEntity();
        var child = new SceneEntity();
        var grandchild = new SceneEntity();
        child.Parent = parent;
        grandchild.Parent = child;

        parent.Transform = new Transform3D(Basis.Identity, new Vector3(10.0f, 0.0f, 0.0f));

        Assert.AreEqual(10.0f, child.Transform.Origin.X);
        Assert.AreEqual(10.0f, grandchild.Transform.Origin.X);
    }

    [EditorTest(Category = "Scene", Thread = TestThread.Background)]
    public static void Transform_commands_pin_and_restore_descendants()
    {
        var parent = new SceneEntity();
        var child = new SceneEntity();
        child.Parent = parent;
        Transform3D before = Transform3D.Identity;
        Transform3D after = new(Basis.Identity, new Vector3(4.0f, 0.0f, 0.0f));
        parent.Transform = after;

        var command = new TransformEntitiesCommand([parent], [before], [after]);

        Assert.IsTrue(command.Targets.Contains(child), "moving a parent should persist its moved child");
        command.Revert();
        Assert.AreEqual(0.0f, parent.Transform.Origin.X);
        Assert.AreEqual(0.0f, child.Transform.Origin.X);
    }

    [EditorTest(Category = "Scene", Thread = TestThread.Background)]
    public static void Parenting_to_a_descendant_is_rejected()
    {
        var parent = new SceneEntity();
        var child = new SceneEntity { Parent = parent };

        Assert.Throws<InvalidOperationException>(() => new SetSceneEntityParentCommand(parent, null, child));
    }
}
