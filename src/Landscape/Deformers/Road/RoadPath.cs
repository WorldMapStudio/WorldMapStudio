using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>One flattened piece of a road's centreline, in the network's own local space.</summary>
public readonly record struct RoadSegment(Vector3 A, Vector3 B);

/// <summary>
/// Turns a <see cref="VertexNetwork"/> into flattened centreline segments and a coverage profile for
/// a road's centre and shoulder textures — entirely in the network's own local space, with no Godot
/// node, entity or chunk dependency. That is what lets it be built once on the main thread and read
/// safely from a background chunk build (see <see cref="ProceduralComponent"/>), and what makes two chunks
/// sharing a border agree on coverage: both read the same immutable segments.
///
/// The graph-to-chains decomposition and the centripetal Catmull-Rom flattening itself are
/// <see cref="NetworkSplineChains"/> — shared with anything else that turns an authored network into a
/// flattened polyline (a river's centerline, for one). What stays here is Road-specific: collapsing the
/// flattened segments to the XZ plane (a road's authored network is Y = 0 by the tool's own convention,
/// so this is a no-op in practice, but the coverage maths below is written assuming it), and the
/// centre/shoulder coverage profile itself. Coverage at a point is the profile of the single nearest
/// segment across every chain, which is what makes forks and crossings union into a smooth join for
/// free: nothing has to special-case a junction, because the nearest-segment distance already blends
/// continuously across one.
/// </summary>
public sealed class RoadPath
{
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
        foreach (NetworkSplineSegment segment in NetworkSplineChains.Flatten(network))
        {
            segments.Add(new RoadSegment(segment.A, segment.B));
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
