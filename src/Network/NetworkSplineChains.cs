using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>One flattened piece of a <see cref="VertexNetwork"/> chain, in the network's own local
/// space, with every authored component (including height) preserved.</summary>
public readonly record struct NetworkSplineSegment(Vector3 A, Vector3 B);

/// <summary>One chain's flattened points, in order, with every authored component (including height)
/// preserved. For a closed chain, <see cref="Points"/> does not repeat the first point at the end —
/// callers that need the closing segment add <c>Points[^1] -&gt; Points[0]</c> themselves, guarded by
/// <see cref="IsClosed"/>.</summary>
public sealed record NetworkSplinePolyline(IReadOnlyList<Vector3> Points, bool IsClosed);

/// <summary>
/// Decomposes a <see cref="VertexNetwork"/>'s graph into chains — runs of edges between junctions
/// (vertices whose degree is not 2) — and flattens each into a centripetal (alpha = 0.5) Catmull-Rom
/// polyline via the Barry-Goldman formula. Centripetal rather than uniform because uniform
/// parameterization cusps and self-intersects on tight turns.
///
/// Every vertex component flows through the blend unchanged, height included: this utility never
/// discards it. A caller that wants a planar result (e.g. <c>RoadPath</c>, whose authored networks are
/// Y = 0 by the tool's own convention) gets that for free from its input rather than needing this to
/// flatten anything — and a caller that wants real elevation and a connected polyline (e.g. a river's
/// ribbon, which needs ordered points to compute a stable perpendicular offset along the path) gets that
/// too, from the same code path via <see cref="FlattenChains"/>. Extracted out of <c>RoadPath</c>
/// (<c>Landscape/Deformers/Road/RoadPath.cs</c>) once a second consumer needed the identical graph/spline
/// maths with a different flattening policy; <c>RoadPath</c> now calls into this rather than owning its
/// own copy.
/// </summary>
public static class NetworkSplineChains
{
    private const float FlattenStep = 2.0f;
    private const int MinSubdivisions = 2;
    private const int MaxSubdivisions = 32;
    private const float CentripetalAlpha = 0.5f;

    /// <summary>Every chain in <paramref name="network"/>, each flattened to an ordered polyline.
    /// Enumeration order across chains (junctions ascending by id, each junction's edges ascending by
    /// id) is fixed so two machines walking the same network always produce the same chains in the same
    /// order — required for build determinism.</summary>
    public static List<NetworkSplinePolyline> FlattenChains(VertexNetwork network)
    {
        var result = new List<NetworkSplinePolyline>();
        foreach (Chain chain in ExtractChains(network))
        {
            List<Vector3> points = FlattenChainPoints(chain);
            if (points.Count >= 2)
            {
                result.Add(new NetworkSplinePolyline(points, chain.IsClosed));
            }
        }

        return result;
    }

    /// <summary>Every chain's segments pooled into one flat, unordered bag — what a distance-to-nearest-
    /// segment query (e.g. <c>RoadPath.DistanceToPath</c>) needs and nothing more. Prefer
    /// <see cref="FlattenChains"/> when connectivity along a chain matters.</summary>
    public static List<NetworkSplineSegment> Flatten(VertexNetwork network)
    {
        var segments = new List<NetworkSplineSegment>();
        foreach (NetworkSplinePolyline polyline in FlattenChains(network))
        {
            IReadOnlyList<Vector3> points = polyline.Points;
            for (int i = 0; i < points.Count - 1; i++)
            {
                segments.Add(new NetworkSplineSegment(points[i], points[i + 1]));
            }

            if (polyline.IsClosed)
            {
                segments.Add(new NetworkSplineSegment(points[^1], points[0]));
            }
        }

        return segments;
    }

    private sealed record Chain(IReadOnlyList<Vector3> Points, bool IsClosed);

    private static List<Chain> ExtractChains(VertexNetwork network)
    {
        Dictionary<int, List<(int Neighbour, int EdgeId)>> adjacency = BuildAdjacency(network);
        var visitedEdges = new HashSet<int>();
        var chains = new List<Chain>();

        List<int> junctionIds = network.Vertices
            .Where(vertex => adjacency[vertex.Id].Count != 2)
            .Select(vertex => vertex.Id)
            .OrderBy(id => id)
            .ToList();

        foreach (int junctionId in junctionIds)
        {
            foreach ((int neighbour, int edgeId) in adjacency[junctionId].OrderBy(entry => entry.EdgeId))
            {
                if (visitedEdges.Contains(edgeId))
                {
                    continue;
                }

                chains.Add(WalkChain(network, adjacency, visitedEdges, junctionId, neighbour, edgeId));
            }
        }

        // Whatever is left touches no junction at all, so it can only be closed loops of degree-2
        // vertices. Peel one off at a time: seed from the lowest unvisited edge, find its whole loop by
        // walking unvisited edges, then re-walk from the loop's lowest vertex id so loop enumeration is
        // deterministic too.
        while (network.Edges.FirstOrDefault(edge => !visitedEdges.Contains(edge.Id)) is { } seed)
        {
            HashSet<int> loopVertices = CollectLoopVertices(adjacency, visitedEdges, seed.A);
            int start = loopVertices.Min();
            List<(int Neighbour, int EdgeId)> options = adjacency[start]
                .Where(entry => !visitedEdges.Contains(entry.EdgeId))
                .OrderBy(entry => entry.EdgeId)
                .ToList();

            if (options.Count == 0)
            {
                // Malformed input guard: nothing left to walk from the chosen start. Drop the seed edge
                // so the loop always makes progress instead of spinning forever.
                visitedEdges.Add(seed.Id);
                continue;
            }

            chains.Add(WalkChain(network, adjacency, visitedEdges, start, options[0].Neighbour, options[0].EdgeId));
        }

        return chains;
    }

    private static Dictionary<int, List<(int Neighbour, int EdgeId)>> BuildAdjacency(VertexNetwork network)
    {
        var adjacency = network.Vertices.ToDictionary(vertex => vertex.Id, _ => new List<(int, int)>());
        foreach (NetworkEdge edge in network.Edges)
        {
            adjacency[edge.A].Add((edge.B, edge.Id));
            adjacency[edge.B].Add((edge.A, edge.Id));
        }

        return adjacency;
    }

    private static HashSet<int> CollectLoopVertices(Dictionary<int, List<(int Neighbour, int EdgeId)>> adjacency, HashSet<int> visitedEdges, int start)
    {
        var found = new HashSet<int> { start };
        var stack = new Stack<int>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            int current = stack.Pop();
            foreach ((int neighbour, int edgeId) in adjacency[current])
            {
                if (visitedEdges.Contains(edgeId) || !found.Add(neighbour))
                {
                    continue;
                }

                stack.Push(neighbour);
            }
        }

        return found;
    }

    /// <summary>
    /// Walks one chain starting at <paramref name="startVertexId"/> via <paramref name="firstEdgeId"/>,
    /// continuing through degree-2 vertices until it either reaches a real junction (open chain) or
    /// returns to its own start (closed loop) — the same walk serves both, since a loop is exactly a
    /// chain with no junction to stop it.
    /// </summary>
    private static Chain WalkChain(
        VertexNetwork network,
        Dictionary<int, List<(int Neighbour, int EdgeId)>> adjacency,
        HashSet<int> visitedEdges,
        int startVertexId,
        int firstNeighbourId,
        int firstEdgeId)
    {
        var points = new List<Vector3> { network.Vertex(startVertexId)!.Position };
        visitedEdges.Add(firstEdgeId);
        int previous = startVertexId;
        int current = firstNeighbourId;
        points.Add(network.Vertex(current)!.Position);

        while (true)
        {
            if (current == startVertexId)
            {
                points.RemoveAt(points.Count - 1);
                return new Chain(points, IsClosed: true);
            }

            List<(int Neighbour, int EdgeId)> neighbours = adjacency[current];
            if (neighbours.Count != 2)
            {
                return new Chain(points, IsClosed: false);
            }

            (int Neighbour, int EdgeId) chosen = neighbours[0].Neighbour == previous ? neighbours[1] : neighbours[0];
            if (visitedEdges.Contains(chosen.EdgeId))
            {
                // Guards against spinning on malformed data; a well-formed walk never revisits an edge.
                return new Chain(points, IsClosed: false);
            }

            visitedEdges.Add(chosen.EdgeId);
            previous = current;
            current = chosen.Neighbour;
            points.Add(network.Vertex(current)!.Position);
        }
    }

    /// <summary>
    /// Flattens one chain to an ordered point list via centripetal (alpha = 0.5) Catmull-Rom, evaluated
    /// with the Barry-Goldman formula. Centripetal rather than uniform because uniform parameterization
    /// cusps and self-intersects on tight turns. Consecutive segments connect exactly (each segment's
    /// last subdivided point is the next segment's first control point), so appending every segment's
    /// subdivided points after one shared starting point yields one continuous polyline.
    /// </summary>
    private static List<Vector3> FlattenChainPoints(Chain chain)
    {
        IReadOnlyList<Vector3> points = chain.Points;
        if (points.Count < 2)
        {
            return [];
        }

        List<Vector3> extended;
        int segmentCount;
        if (chain.IsClosed)
        {
            int n = points.Count;
            extended = new List<Vector3>(n + 3) { points[n - 1] };
            extended.AddRange(points);
            extended.Add(points[0]);
            extended.Add(points[1 % n]);
            segmentCount = n;
        }
        else
        {
            extended = new List<Vector3>(points.Count + 2) { Extrapolate(points[0], points[1]) };
            extended.AddRange(points);
            extended.Add(Extrapolate(points[^1], points[^2]));
            segmentCount = points.Count - 1;
        }

        var result = new List<Vector3> { extended[1] };
        for (int i = 0; i < segmentCount; i++)
        {
            Vector3 p0 = extended[i];
            Vector3 p1 = extended[i + 1];
            Vector3 p2 = extended[i + 2];
            Vector3 p3 = extended[i + 3];

            int subdivisions = Mathf.Clamp(Mathf.CeilToInt(p1.DistanceTo(p2) / FlattenStep), MinSubdivisions, MaxSubdivisions);
            for (int s = 1; s <= subdivisions; s++)
            {
                float t = (float)s / subdivisions;
                result.Add(EvaluateCentripetal(p0, p1, p2, p3, t));
            }
        }

        // A closed chain's last subdivided point lands back on extended[1] (the shared starting point)
        // by construction — drop the duplicate so Points has exactly one sample per position around the
        // loop, matching the "does not repeat the first point" contract callers rely on.
        if (chain.IsClosed && result.Count > 1 && result[^1].DistanceSquaredTo(result[0]) < 1e-6f)
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    private static Vector3 Extrapolate(Vector3 p0, Vector3 p1) => (2.0f * p0) - p1;

    private static Vector3 EvaluateCentripetal(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        const float t0 = 0.0f;
        float t1 = t0 + KnotInterval(p0, p1);
        float t2 = t1 + KnotInterval(p1, p2);
        float t3 = t2 + KnotInterval(p2, p3);
        float u = t1 + ((t2 - t1) * t);

        Vector3 a1 = Blend(p0, p1, t0, t1, u);
        Vector3 a2 = Blend(p1, p2, t1, t2, u);
        Vector3 a3 = Blend(p2, p3, t2, t3, u);
        Vector3 b1 = Blend(a1, a2, t0, t2, u);
        Vector3 b2 = Blend(a2, a3, t1, t3, u);
        return Blend(b1, b2, t1, t2, u);
    }

    private static float KnotInterval(Vector3 a, Vector3 b) =>
        Mathf.Max(Mathf.Pow(a.DistanceTo(b), CentripetalAlpha), 1e-4f);

    private static Vector3 Blend(Vector3 a, Vector3 b, float ta, float tb, float t)
    {
        float span = tb - ta;
        return span <= 1e-8f ? a : a + ((b - a) * ((t - ta) / span));
    }
}
