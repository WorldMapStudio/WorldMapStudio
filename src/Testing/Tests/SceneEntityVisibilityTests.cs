using Godot;

namespace WorldMapStudio;

/// <summary>
/// Covers <see cref="SceneEntity.Visible"/> — the mirrored node-visibility flag a view category
/// flips without ever destroying or rebuilding the entity's representation.
/// </summary>
public static class SceneEntityVisibilityTests
{
    private sealed class NodeEntity : SceneEntity
    {
        public Node3D? RepresentationNode => Node;

        protected override Node3D BuildNode() => new() { Name = "Test" };
    }

    [EditorTest(Category = "SceneEntity", Thread = TestThread.Background)]
    public static void Hiding_a_represented_entity_writes_the_node_flag()
    {
        var entity = new NodeEntity();
        entity.CreateRepresentation(new Node3D());

        entity.Visible = false;

        Assert.IsFalse(entity.RepresentationNode!.Visible);
    }

    [EditorTest(Category = "SceneEntity", Thread = TestThread.Background)]
    public static void The_flag_survives_a_representation_rebuild()
    {
        var entity = new NodeEntity();
        var parent = new Node3D();
        entity.CreateRepresentation(parent);
        entity.Visible = false;

        entity.DestroyRepresentation();
        entity.CreateRepresentation(parent);

        Assert.IsFalse(entity.Visible, "the flag itself is untouched by a rebuild");
        Assert.IsFalse(entity.RepresentationNode!.Visible, "and CreateRepresentation applies it to the new node");
    }

    [EditorTest(Category = "SceneEntity", Thread = TestThread.Background)]
    public static void Hiding_an_unrepresented_entity_does_not_throw()
    {
        var entity = new NodeEntity();
        entity.Visible = false;
        Assert.IsFalse(entity.Visible);
    }
}
