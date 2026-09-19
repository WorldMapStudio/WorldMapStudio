using Godot;

namespace WorldMapStudio;

/// <summary>
/// Covers <see cref="MeshPicking"/>, which backs click selection. The point of picking against
/// triangles rather than the bounding box is that a click has to miss when it passes through a gap
/// in the model, so both the hit and the miss are worth pinning down — as is triangle extraction
/// itself, since a silently empty result would make every model unselectable rather than merely
/// imprecise.
/// </summary>
public static class MeshPickingTests
{
    /// <summary>A unit quad in the XY plane at z = 0, as two indexed triangles.</summary>
    private static ArrayMesh BuildQuad()
    {
        Vector3[] vertices =
        [
            new(-1.0f, -1.0f, 0.0f),
            new(1.0f, -1.0f, 0.0f),
            new(1.0f, 1.0f, 0.0f),
            new(-1.0f, 1.0f, 0.0f),
        ];
        int[] indices = [0, 1, 2, 0, 2, 3];

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    [EditorTest(Category = "MeshPicking", Thread = TestThread.Background)]
    public static void Indexed_surfaces_yield_their_triangles()
    {
        // Extraction returning nothing would turn "the click missed" and "there was nothing to test"
        // into the same answer.
        Vector3[] triangles = MeshPicking.Triangles(BuildQuad());

        Assert.AreEqual(6, triangles.Length, "two triangles, three vertices each");
    }

    [EditorTest(Category = "MeshPicking", Thread = TestThread.Background)]
    public static void Non_indexed_surfaces_yield_their_triangles()
    {
        Vector3[] vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)];
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        Assert.AreEqual(3, MeshPicking.Triangles(mesh).Length);
    }

    [EditorTest(Category = "MeshPicking", Thread = TestThread.Background)]
    public static void A_ray_through_the_face_hits_at_its_distance()
    {
        Vector3[] triangles = MeshPicking.Triangles(BuildQuad());
        float best = float.PositiveInfinity;

        bool hit = MeshPicking.TryRayTriangles(triangles, new Vector3(0.0f, 0.0f, -5.0f), new Vector3(0.0f, 0.0f, 1.0f), ref best);

        Assert.IsTrue(hit, "a ray aimed at the middle of the quad hits it");
        Assert.AreApproximatelyEqual(5.0, best, 1e-4, "the quad sits 5 units along the ray");
    }

    [EditorTest(Category = "MeshPicking", Thread = TestThread.Background)]
    public static void A_ray_beside_the_face_misses()
    {
        // Inside the quad's bounding box on Y, but well outside it on X: the box test would call
        // this a hit, which is exactly the wonkiness triangle picking exists to remove.
        Vector3[] triangles = MeshPicking.Triangles(BuildQuad());
        float best = float.PositiveInfinity;

        bool hit = MeshPicking.TryRayTriangles(triangles, new Vector3(3.0f, 0.0f, -5.0f), new Vector3(0.0f, 0.0f, 1.0f), ref best);

        Assert.IsFalse(hit, "the ray passes beside the geometry");
        Assert.IsTrue(float.IsPositiveInfinity(best), "a miss leaves the running best alone");
    }

    [EditorTest(Category = "MeshPicking", Thread = TestThread.Background)]
    public static void Geometry_behind_the_ray_origin_misses()
    {
        Vector3[] triangles = MeshPicking.Triangles(BuildQuad());
        float best = float.PositiveInfinity;

        // Origin past the quad, aimed further away from it.
        bool hit = MeshPicking.TryRayTriangles(triangles, new Vector3(0.0f, 0.0f, 5.0f), new Vector3(0.0f, 0.0f, 1.0f), ref best);

        Assert.IsFalse(hit, "picking never selects what is behind the camera");
    }

    [EditorTest(Category = "MeshPicking", Thread = TestThread.Background)]
    public static void Faces_are_hit_from_either_side()
    {
        // Single-sided geometry is common in WoW models; culling the pick by winding would make it
        // unselectable from whichever side the artist did not face.
        Vector3[] triangles = MeshPicking.Triangles(BuildQuad());

        float front = float.PositiveInfinity;
        bool fromFront = MeshPicking.TryRayTriangles(triangles, new Vector3(0.0f, 0.0f, -5.0f), new Vector3(0.0f, 0.0f, 1.0f), ref front);

        float back = float.PositiveInfinity;
        bool fromBack = MeshPicking.TryRayTriangles(triangles, new Vector3(0.0f, 0.0f, 5.0f), new Vector3(0.0f, 0.0f, -1.0f), ref back);

        Assert.IsTrue(fromFront, "hit from the front");
        Assert.IsTrue(fromBack, "hit from the back");
    }

    [EditorTest(Category = "MeshPicking", Thread = TestThread.Background)]
    public static void An_unnormalised_direction_reports_distance_along_that_ray()
    {
        // What lets a hit in a scaled mesh's local space be compared against hits on other entities:
        // the caller transforms the ray without renormalising, so t stays in world-ray units.
        Vector3[] triangles = MeshPicking.Triangles(BuildQuad());
        float best = float.PositiveInfinity;

        // Direction of length 2 => the quad 10 units away is reached at t = 5.
        MeshPicking.TryRayTriangles(triangles, new Vector3(0.0f, 0.0f, -10.0f), new Vector3(0.0f, 0.0f, 2.0f), ref best);

        Assert.AreApproximatelyEqual(5.0, best, 1e-4);
    }

    [EditorTest(Category = "MeshPicking", Thread = TestThread.Background)]
    public static void The_nearest_of_several_faces_wins()
    {
        Vector3[] triangles = MeshPicking.Triangles(BuildQuad());

        // Near quad at z = 0 (t = 5), far quad shifted to z = 10 (t = 15) by offsetting the ray.
        float best = float.PositiveInfinity;
        MeshPicking.TryRayTriangles(triangles, new Vector3(0.0f, 0.0f, -15.0f), new Vector3(0.0f, 0.0f, 1.0f), ref best);
        MeshPicking.TryRayTriangles(triangles, new Vector3(0.0f, 0.0f, -5.0f), new Vector3(0.0f, 0.0f, 1.0f), ref best);

        Assert.AreApproximatelyEqual(5.0, best, 1e-4, "the running best only ever shrinks");
    }
}
