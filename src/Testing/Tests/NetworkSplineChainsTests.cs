using Godot;

namespace WorldMapStudio;

/// <summary>
/// Covers the one property <see cref="NetworkSplineChains"/> exists to add over the code it was
/// extracted out of (<c>RoadPath</c>, still covered end-to-end by <c>RoadTests</c>): height flows
/// through the flatten unchanged instead of being discarded. Chain extraction, forks, closed loops and
/// determinism are already exercised by <c>RoadTests</c> via <c>RoadPath.Build</c>, which now delegates
/// here — this file does not re-cover that ground.
/// </summary>
public static class NetworkSplineChainsTests
{
    [EditorTest(Category = "Network", Thread = TestThread.Background)]
    public static void Flatten_preserves_authored_height_along_a_slope()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(4.0f, 2.0f, 0.0f));
        int c = network.AddVertex(new Vector3(8.0f, 4.0f, 0.0f));
        network.AddEdge(a, b);
        network.AddEdge(b, c);

        var segments = NetworkSplineChains.Flatten(network);

        Assert.Greater(segments.Count, 0);
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        foreach (NetworkSplineSegment segment in segments)
        {
            minY = Mathf.Min(minY, Mathf.Min(segment.A.Y, segment.B.Y));
            maxY = Mathf.Max(maxY, Mathf.Max(segment.A.Y, segment.B.Y));
        }

        Assert.AreApproximatelyEqual(0.0, minY, 0.1, "the flattened polyline should start near the low end's authored height");
        Assert.AreApproximatelyEqual(4.0, maxY, 0.1, "the flattened polyline should reach the high end's authored height");
    }

    [EditorTest(Category = "Network", Thread = TestThread.Background)]
    public static void Flatten_passes_through_every_authored_vertex_in_3d()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(4.0f, 3.0f, 1.0f));
        int c = network.AddVertex(new Vector3(8.0f, 1.0f, -2.0f));
        network.AddEdge(a, b);
        network.AddEdge(b, c);

        var segments = NetworkSplineChains.Flatten(network);

        foreach (int id in new[] { a, b, c })
        {
            Vector3 target = network.Vertex(id)!.Position;
            float best = float.PositiveInfinity;
            foreach (NetworkSplineSegment segment in segments)
            {
                best = Mathf.Min(best, segment.A.DistanceTo(target));
                best = Mathf.Min(best, segment.B.DistanceTo(target));
            }

            Assert.AreApproximatelyEqual(0.0, best, 0.05, $"the spline should pass through vertex {id} in full 3D, not just its XZ projection");
        }
    }
}
