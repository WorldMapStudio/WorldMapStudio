using Godot;

namespace WorldMapStudio;

/// <summary>
/// Covers <see cref="SceneEntity.WorldBounds"/>, which the streaming scan and the landscape system
/// both query against. A rotated entity has to report the box that <em>encloses</em> it, or an
/// overlap test misses entities that reach into a region.
/// </summary>
public static class SceneEntityBoundsTests
{
    private sealed class BoundsComponent(Aabb bounds) : SceneComponent, ISceneBoundsProvider
    {
        public override string TypeId => "test-bounds";

        public override string DisplayName => "Test Bounds";

        public Aabb LocalBounds { get; } = bounds;
    }

    private sealed class BoxEntity : SceneEntity
    {
        public required Aabb Local { get; init; }

        public override SelfRotation SelfRotation => SelfRotation.Full;

        public override Aabb LocalBounds => Local;

        protected override Node3D BuildNode() => new();
    }

    [EditorTest(Category = "SceneEntity", Thread = TestThread.Background)]
    public static void World_bounds_follow_the_transform()
    {
        var entity = new BoxEntity { Local = new Aabb(new Vector3(-1.0f, -1.0f, -1.0f), new Vector3(2.0f, 2.0f, 2.0f)) };
        entity.Transform = new Transform3D(Basis.Identity, new Vector3(10.0f, 0.0f, -5.0f));

        Aabb bounds = entity.WorldBounds;

        Assert.AreApproximatelyEqual(9.0, bounds.Position.X, 1e-4);
        Assert.AreApproximatelyEqual(-6.0, bounds.Position.Z, 1e-4);
        Assert.AreApproximatelyEqual(11.0, bounds.End.X, 1e-4);
        Assert.AreApproximatelyEqual(-4.0, bounds.End.Z, 1e-4);
    }

    [EditorTest(Category = "SceneEntity", Thread = TestThread.Background)]
    public static void World_bounds_enclose_a_rotated_entity()
    {
        // A 2x2 square yawed 45° needs a box of 2*sqrt(2) to contain its corners. Reporting the
        // unrotated extent here would let an overlap query miss the entity near its diagonal.
        var entity = new BoxEntity { Local = new Aabb(new Vector3(-1.0f, -1.0f, -1.0f), new Vector3(2.0f, 2.0f, 2.0f)) };
        entity.Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Pi * 0.25f), Vector3.Zero);

        Aabb bounds = entity.WorldBounds;

        Assert.AreApproximatelyEqual(Mathf.Sqrt2 * 2.0, bounds.Size.X, 1e-4);
        Assert.AreApproximatelyEqual(Mathf.Sqrt2 * 2.0, bounds.Size.Z, 1e-4);
        Assert.AreApproximatelyEqual(2.0, bounds.Size.Y, 1e-4, "the rotation axis is unaffected");
    }

    [EditorTest(Category = "SceneEntity", Thread = TestThread.Background)]
    public static void An_offset_entity_reaches_beyond_its_origin()
    {
        // The case the point-based scan got wrong: the origin sits outside the region while the
        // entity itself reaches into it.
        var entity = new BoxEntity { Local = new Aabb(new Vector3(-20.0f, -1.0f, -1.0f), new Vector3(40.0f, 2.0f, 2.0f)) };
        entity.Transform = new Transform3D(Basis.Identity, new Vector3(30.0f, 0.0f, 0.0f));

        var region = new Aabb(new Vector3(-5.0f, -5.0f, -5.0f), new Vector3(10.0f, 10.0f, 10.0f));

        Assert.IsFalse(region.HasPoint(entity.Transform.Origin), "the origin is outside the region");
        Assert.IsTrue(region.Intersects(entity.WorldBounds), "but its bounds overlap it");
    }

    [EditorTest(Category = "SceneEntity", Thread = TestThread.Background)]
    public static void Effective_bounds_merge_component_extents()
    {
        var entity = new SceneEntity();
        entity.AddComponent(new BoundsComponent(new Aabb(Vector3.Zero, new Vector3(2.0f, 10.0f, 4.0f))));
        entity.AddComponent(new BoundsComponent(new Aabb(Vector3.Zero, new Vector3(12.0f, 2.0f, 6.0f))));

        Aabb bounds = entity.LocalBounds;

        Assert.AreApproximatelyEqual(0.0, bounds.Position.X, 1e-4);
        Assert.AreApproximatelyEqual(0.0, bounds.Position.Y, 1e-4);
        Assert.AreApproximatelyEqual(0.0, bounds.Position.Z, 1e-4);
        Assert.AreApproximatelyEqual(12.0, bounds.Size.X, 1e-4);
        Assert.AreApproximatelyEqual(10.0, bounds.Size.Y, 1e-4);
        Assert.AreApproximatelyEqual(6.0, bounds.Size.Z, 1e-4);
    }
}
