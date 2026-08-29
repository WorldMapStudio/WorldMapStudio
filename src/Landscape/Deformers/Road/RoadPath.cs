using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>One flattened piece of a road's centreline, in the network's own local space.</summary>
public readonly record struct RoadSegment(Vector3 A, Vector3 B);

/// <summary>
/// Turns a <see cref="VertexNetwork"/> into flattened centreline segments and a coverage profile for
/// a road's centre and shoulder textures — entirely in the network's own local space, with no Godot
/// node, entity or chunk dependency. That is what lets it be built once on the main thread and read
/// safely from a background chunk build (see <see cref="RoadComponent"/>), and what makes two chunks
/// sharing a border agree on coverage: both read the same immutable segments.
///
/// The network is a graph, not a sequence, so it is decomposed into <b>chains</b> first — runs of
/// edges between junctions (vertices whose degree is not 2) — and each chain becomes one centripetal
/// Catmull-Rom spline. Coverage at a point is the profile of the single nearest segment across every
/// chain, which is what makes forks and crossings union into a smooth join for free: nothing has to
/// special-case a junction, because the nearest-segment distance already blends continuously across
/// one.
/// </summary>
public sealed class RoadPath
{
    private const float FlattenStep = 2.0f;
    private const int MinSubdivisions = 2;
    private const int MaxSubdivisions = 32;
    private const float CentripetalAlpha = 0.5f;

    private RoadPath(IReadOnlyList<RoadSegment> segments, float centreHalf, float outerRadius, float falloff, Aabb localBounds)
    {
        Segments = segments;
        CentreHalf = centreHalf;
        OuterRadius = outerRadius;
        Falloff = falloff;
        LocalBounds = localBounds;
    }

    /// <summary>The flattened centreline, local space, in no particular order across chains.</summary>
    public IReadOnlyList<RoadSegment> Segments { get; }

    /// <summary>Half of the centre texture's width — the radius at which centre coverage reaches zero.</summary>
    public float CentreHalf { get; }

    /// <summary>Centre half-width plus shoulder width — the radius at which shoulder coverage reaches zero.</summary>
    public float OuterRadius { get; }

    public float Falloff { get; }

    /// <summary>The network's XZ extent grown by <see cref="OuterRadius"/>, flattened to Y = 0.</summary>
    public Aabb LocalBounds { get; }

    public static RoadPath Build(VertexNetwork network, float centreWidth, float shoulderWidth, float falloff)
    {
        float centreHalf = Mathf.Max(0.0f, centreWidth * 0.5f);
        float outerRadius = Mathf.Max(centreHalf, centreHalf + Mathf.Max(0.0f, shoulderWidth));
        falloff = Mathf.Clamp(falloff, 0.0f, 1.0f);

        var segments = new List<RoadSegment>();
        foreach (RoadChain chain in ExtractChains(network))
        {
            FlattenChain(chain, segments);
        }

        Aabb bounds = ComputeBounds(segments, outerRadius);
        return new RoadPath(segments, centreHalf, outerRadius, falloff, bounds);
    }

    /// <summary>Distance in the XZ plane from a local point to the nearest centreline segment. Height
    /// is ignored on both sides — the data is authored flat, and the query point's height (usually the
    /// terrain height at that point) carries no meaning for coverage.</summary>
    public float DistanceToPath(Vector3 local)
    {
        float best = float.PositiveInfinity;
        foreach (RoadSegment segment in Segments)
        {
            float distance = DistanceToSegmentXZ(local, segment.A, segment.B);
            if (distance < best)
            {
                best = distance;
            }
        }

        return best;
    }

    public float CentreCoverage(Vector3 local) => Weight(DistanceToPath(local), CentreHalf, Falloff);

    public float ShoulderCoverage(Vector3 local) => Weight(DistanceToPath(local), OuterRadius, Falloff);

    /// <summary>Coverage profile shared by both channels: 1 within the solid core, smoothstepped to 0
    /// across the falloff band, 0 beyond <paramref name="radius"/>. Same shape as
    /// <see cref="StampComponent"/>'s radial weight, parameterized so centre and shoulder reuse it.</summary>
    public static float Weight(float distance, float radius, float falloff)
    {
        if (radius <= 0.0f || distance >= radius)
        {
            return 0.0f;
        }

        float solid = radius * (1.0f - falloff);
        if (distance <= solid)
        {
            return 1.0f;
        }

        float fade = radius - solid;
        return fade <= 0.0f ? 1.0f : Mathf.SmoothStep(0.0f, 1.0f, 1.0f - ((distance - solid) / fade));
    }

    public static float DistanceToSegmentXZ(Vector3 point, Vector3 a, Vector3 b)
    {
        float dx = b.X - a.X;
        float dz = b.Z - a.Z;
        float lenSq = (dx * dx) + (dz * dz);
        float t = lenSq < 1e-10f ? 0.0f : Mathf.Clamp((((point.X - a.X) * dx) + ((point.Z - a.Z) * dz)) / lenSq, 0.0f, 1.0f);
        float cx = a.X + (dx * t);
        float cz = a.Z + (dz * t);
        float ddx = point.X - cx;
        float ddz = point.Z - cz;
        return Mathf.Sqrt((ddx * ddx) + (ddz * ddz));
    }

    private sealed record RoadChain(IReadOnlyList<Vector3> Points, bool IsClosed);

    /// <summary>
    /// Decomposes the graph into chains. Junctions (degree != 2) are visited in ascending vertex id,
    /// and each junction's incident edges in ascending edge id, so two machines walking the same
    /// network always produce the same chains in the same order — required for build determinism, same
    /// as <see cref="LandscapeClaimGroup.Key"/>.
    /// </summary>
    private static List<RoadChain> ExtractChains(VertexNetwork network)
    {
        Dictionary<int, List<(int Neighbour, int EdgeId)>> adjacency = BuildAdjacency(network);
        var visitedEdges = new HashSet<int>();
        var chains = new List<RoadChain>();

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
    private static RoadChain WalkChain(
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
                return new RoadChain(points, IsClosed: true);
            }

            List<(int Neighbour, int EdgeId)> neighbours = adjacency[current];
            if (neighbours.Count != 2)
            {
                return new RoadChain(points, IsClosed: false);
            }

            (int Neighbour, int EdgeId) chosen = neighbours[0].Neighbour == previous ? neighbours[1] : neighbours[0];
            if (visitedEdges.Contains(chosen.EdgeId))
            {
                // Guards against spinning on malformed data; a well-formed walk never revisits an edge.
                return new RoadChain(points, IsClosed: false);
            }

            visitedEdges.Add(chosen.EdgeId);
            previous = current;
            current = chosen.Neighbour;
            points.Add(network.Vertex(current)!.Position);
        }
    }

    /// <summary>
    /// Flattens one chain to line segments via centripetal (alpha = 0.5) Catmull-Rom, evaluated with
    /// the Barry-Goldman formula. Centripetal rather than uniform because uniform parameterization
    /// cusps and self-intersects on the tight turns a road actually has.
    /// </summary>
    private static void FlattenChain(RoadChain chain, List<RoadSegment> output)
    {
        IReadOnlyList<Vector3> points = chain.Points;
        if (points.Count < 2)
        {
            return;
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

        for (int i = 0; i < segmentCount; i++)
        {
            Vector3 p0 = extended[i];
            Vector3 p1 = extended[i + 1];
            Vector3 p2 = extended[i + 2];
            Vector3 p3 = extended[i + 3];

            int subdivisions = Mathf.Clamp(Mathf.CeilToInt(p1.DistanceTo(p2) / FlattenStep), MinSubdivisions, MaxSubdivisions);
            Vector3 last = p1;
            for (int s = 1; s <= subdivisions; s++)
            {
                float t = (float)s / subdivisions;
                Vector3 point = EvaluateCentripetal(p0, p1, p2, p3, t);
                output.Add(new RoadSegment(last, point));
                last = point;
            }
        }
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

    private static Aabb ComputeBounds(List<RoadSegment> segments, float outerRadius)
    {
        Vector3 min;
        Vector3 max;
        if (segments.Count == 0)
        {
            min = Vector3.Zero;
            max = Vector3.Zero;
        }
        else
        {
            min = segments[0].A;
            max = segments[0].A;
            foreach (RoadSegment segment in segments)
            {
                min = ComponentMin(min, segment.A);
                min = ComponentMin(min, segment.B);
                max = ComponentMax(max, segment.A);
                max = ComponentMax(max, segment.B);
            }
        }

        var growth = new Vector3(outerRadius, 0.0f, outerRadius);
        min -= growth;
        max += growth;
        return new Aabb(min, max - min);
    }

    private static Vector3 ComponentMin(Vector3 a, Vector3 b) =>
        new(Mathf.Min(a.X, b.X), 0.0f, Mathf.Min(a.Z, b.Z));

    private static Vector3 ComponentMax(Vector3 a, Vector3 b) =>
        new(Mathf.Max(a.X, b.X), 0.0f, Mathf.Max(a.Z, b.Z));
}
