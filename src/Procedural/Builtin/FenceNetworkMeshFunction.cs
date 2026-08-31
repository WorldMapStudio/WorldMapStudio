using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Turns a <see cref="VertexNetwork"/> into a post-and-rail fence: a post at every authored vertex,
/// plus evenly spaced posts filled in along any span longer than <see cref="PostSpacing"/>, with
/// straight rails welded between them, given a subtle natural warp by recursive midpoint displacement
/// (see <see cref="WithWaviness"/>) rather than being razor-straight. Vertex height is authored per
/// point (<see cref="SnapToTerrainOnPlace"/> snaps a new point onto the terrain under the cursor,
/// keeping its real height) rather than resampled at build time, so a rail's undulation across a
/// hillside is only as accurate as its posts are close together — the same tradeoff a real fence makes.
/// </summary>
[Subsystem(nameof(ProceduralSystem))]
public sealed class FenceNetworkMeshFunction : IProceduralFunction
{
    public static readonly MeshParameter PostSpacing =
        MeshParameter.Float("post_spacing", "Post spacing", 3.0f, 0.5f, 64.0f, "Target spacing between automatically filled-in posts along a long span.");

    public static readonly MeshParameter PostSize =
        MeshParameter.Float("post_size", "Post size", 0.18f, 0.02f, 4.0f, "Width and depth of each post's square cross-section.");

    public static readonly MeshParameter PostHeight =
        MeshParameter.Float("post_height", "Post height", 1.1f, 0.05f, 16.0f, "Height of a post's flat shoulder above its authored ground point (below its cap, if any).");

    public static readonly MeshParameter PostCapHeight =
        MeshParameter.Float("post_cap_height", "Post cap height", 0.12f, 0.0f, 4.0f, "Height of the pointed cap above a post's shoulder — 0 for a flat-topped post.");

    public static readonly MeshParameter EmbedDepth =
        MeshParameter.Float("embed_depth", "Embed depth", 0.4f, 0.0f, 8.0f, "How far a post extends below its authored ground point.");

    public static readonly MeshParameter RailCount =
        MeshParameter.Int("rail_count", "Rail count", 2, 1, 6, "Number of horizontal rails.");

    public static readonly MeshParameter RailTopOffset =
        MeshParameter.Float("rail_top_offset", "Rail top offset", 0.1f, 0.0f, 8.0f, "Distance from a post's shoulder down to the top rail.");

    public static readonly MeshParameter RailGap =
        MeshParameter.Float("rail_gap", "Rail gap", 0.35f, 0.02f, 8.0f, "Vertical spacing between consecutive rails.");

    public static readonly MeshParameter RailWidth =
        MeshParameter.Float("rail_width", "Rail width", 0.14f, 0.01f, 4.0f, "Rail plank width, across the fence line.");

    public static readonly MeshParameter RailThickness =
        MeshParameter.Float("rail_thickness", "Rail thickness", 0.06f, 0.01f, 4.0f, "Rail plank thickness.");

    public static readonly MeshParameter RailWaviness =
        MeshParameter.Float("rail_waviness", "Rail waviness", 0.05f, 0.0f, 4.0f, "Sideways wobble added to each rail span, like a slightly warped natural plank — 0 for dead straight.");

    public static readonly MeshParameter RailWaveDetail =
        MeshParameter.Int("rail_wave_detail", "Rail wave detail", 2, 0, 5, "How many times each rail span is subdivided to build its wobble — 0 disables it regardless of waviness.");

    public static readonly MeshParameter UvScale =
        MeshParameter.Float("uv_scale", "UV scale", 1.0f, 0.01f, 1024.0f, "Texture repeats per local unit.");

    public static readonly MeshMaterialSlot Surface = new("surface", "Surface", "Fence material — rails and posts share it.");

    public static readonly ProceduralOutputSlot Output = new("mesh", "Mesh", [], [Surface]);

    public string Id => "builtin.mesh.fence_network";

    public string DisplayName => "Fence Network";

    public string Description => "Turns a network into a post-and-rail fence: posts at every vertex, welded to straight rails between them.";

    public int Version => 3;

    public float Priority => 0f;

    /// <summary>New points snap onto the terrain under the cursor — see the class comment.</summary>
    public bool SnapToTerrainOnPlace => true;

    public IReadOnlyList<MeshParameter> Parameters { get; } = MeshParameter.List(
        PostSpacing, PostSize, PostHeight, PostCapHeight, EmbedDepth, RailCount, RailTopOffset, RailGap,
        RailWidth, RailThickness, RailWaviness, RailWaveDetail, UvScale);

    public IReadOnlyList<ProceduralOutputSlot> Outputs { get; } = [Output];

    public FenceNetworkMeshFunction(ProceduralSystem system)
    {
    }

    public void Build(in ProceduralBuildContext context, ProceduralOutputBuilder output)
    {
        float postSpacing = Mathf.Max(0.1f, context.Float(PostSpacing));
        float postSize = Mathf.Max(0.01f, context.Float(PostSize));
        float postHeight = Mathf.Max(0.01f, context.Float(PostHeight));
        float postCapHeight = Mathf.Max(0.0f, context.Float(PostCapHeight));
        float embedDepth = Mathf.Max(0.0f, context.Float(EmbedDepth));
        int railCount = Mathf.Clamp(context.Int(RailCount), 1, 6);
        float railTopOffset = Mathf.Max(0.0f, context.Float(RailTopOffset));
        float railGap = Mathf.Max(0.01f, context.Float(RailGap));
        float railWidth = Mathf.Max(0.01f, context.Float(RailWidth));
        float railThickness = Mathf.Max(0.01f, context.Float(RailThickness));
        float railWaviness = Mathf.Max(0.0f, context.Float(RailWaviness));
        int railWaveDetail = Mathf.Clamp(context.Int(RailWaveDetail), 0, 5);
        float uvScale = Mathf.Max(0.001f, context.Float(UvScale));

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        var postedVertices = new HashSet<int>();
        Dictionary<int, List<(int Other, int EdgeId)>> adjacency = BuildAdjacency(context.Network);

        foreach (NetworkEdge edge in context.Network.Edges)
        {
            NetworkVertex? a = context.Network.Vertex(edge.A);
            NetworkVertex? b = context.Network.Vertex(edge.B);
            if (a == null || b == null)
            {
                continue;
            }

            if (postedVertices.Add(a.Id))
            {
                Vector3 facing = PrimaryDirection(context.Network, adjacency, a, fallback: b.Position - a.Position);
                AddPost(a.Position, postSize, postHeight, postCapHeight, embedDepth, facing, uvScale, vertices, normals, uvs, indices);
            }

            if (postedVertices.Add(b.Id))
            {
                Vector3 facing = PrimaryDirection(context.Network, adjacency, b, fallback: a.Position - b.Position);
                AddPost(b.Position, postSize, postHeight, postCapHeight, embedDepth, facing, uvScale, vertices, normals, uvs, indices);
            }

            List<Vector3> waypoints = Subdivide(a.Position, b.Position, postSpacing);
            Vector3 edgeDirection = b.Position - a.Position;
            for (int i = 1; i < waypoints.Count - 1; i++)
            {
                AddPost(waypoints[i], postSize, postHeight, postCapHeight, embedDepth, edgeDirection, uvScale, vertices, normals, uvs, indices);
            }

            // Cumulative distance from the edge's own start, not reset per filled-in span, so a rail's
            // texture runs continuously along the whole edge instead of seaming at every filled post.
            float travelled = 0.0f;
            for (int i = 0; i < waypoints.Count - 1; i++)
            {
                for (int rail = 0; rail < railCount; rail++)
                {
                    Vector3 railUp = Vector3.Up * (postHeight - railTopOffset - (rail * railGap));
                    var rng = new Random(HashCode.Combine(edge.Id, i, rail));
                    List<Vector3> wavy = WithWaviness(waypoints[i] + railUp, waypoints[i + 1] + railUp, railWaviness, railWaveDetail, rng);
                    float wavyTravelled = travelled;
                    for (int w = 0; w < wavy.Count - 1; w++)
                    {
                        AddBeam(wavy[w], wavy[w + 1], railWidth, railThickness, wavyTravelled, uvScale, vertices, normals, uvs, indices);
                        wavyTravelled += wavy[w].DistanceTo(wavy[w + 1]);
                    }
                }

                travelled += waypoints[i].DistanceTo(waypoints[i + 1]);
            }
        }

        output.AddSurface(Output, "Fence", vertices, indices, context.Material(Output, Surface), normals, uvs);
    }

    /// <summary>Every vertex's incident (neighbour, edge id) pairs, undirected.</summary>
    private static Dictionary<int, List<(int Other, int EdgeId)>> BuildAdjacency(VertexNetwork network)
    {
        var adjacency = network.Vertices.ToDictionary(vertex => vertex.Id, _ => new List<(int, int)>());
        foreach (NetworkEdge edge in network.Edges)
        {
            adjacency[edge.A].Add((edge.B, edge.Id));
            adjacency[edge.B].Add((edge.A, edge.Id));
        }

        return adjacency;
    }

    /// <summary>The direction a post at <paramref name="vertex"/> should face: towards the neighbour of
    /// its lowest-id incident edge. An end post therefore aligns with its one rail, and a straight-through
    /// post aligns with the shared line of both its rails (direction sign does not matter — the post's
    /// cross-section is symmetric under a 180-degree turn). A branching corner deterministically picks
    /// one of its rails rather than an exact angle bisector — a deliberate simplification.</summary>
    private static Vector3 PrimaryDirection(
        VertexNetwork network, Dictionary<int, List<(int Other, int EdgeId)>> adjacency, NetworkVertex vertex, Vector3 fallback)
    {
        if (!adjacency.TryGetValue(vertex.Id, out List<(int Other, int EdgeId)>? neighbours) || neighbours.Count == 0)
        {
            return fallback;
        }

        int otherId = neighbours.OrderBy(neighbour => neighbour.EdgeId).First().Other;
        return network.Vertex(otherId) is { } other ? other.Position - vertex.Position : fallback;
    }

    /// <summary>Points from <paramref name="a"/> to <paramref name="b"/> inclusive, evenly spaced at
    /// roughly <paramref name="spacing"/> apart (including both endpoints), by plain linear
    /// interpolation — there is no terrain to resample against a filled-in point, so its height is
    /// exactly what the straight line between its two authored neighbours already implies.</summary>
    private static List<Vector3> Subdivide(Vector3 a, Vector3 b, float spacing)
    {
        int segments = Mathf.Max(1, Mathf.RoundToInt(a.DistanceTo(b) / spacing));
        var points = new List<Vector3>(segments + 1);
        for (int i = 0; i <= segments; i++)
        {
            points.Add(a.Lerp(b, (float)i / segments));
        }

        return points;
    }

    /// <summary>Recursive midpoint displacement: starting from the exact endpoints (never moved, so a
    /// span still welds cleanly to its posts), each pass inserts a midpoint between every consecutive
    /// pair and nudges it sideways by a random amount, then halves the amplitude for the next pass — the
    /// same fractal terrain-generation technique, applied to a line instead of a heightmap, to give a
    /// straight plank a subtle natural warp instead of a razor-straight edge. Seeded by the caller so a
    /// given span's wobble is stable across rebuilds rather than reshuffling on every edit.</summary>
    private static List<Vector3> WithWaviness(Vector3 a, Vector3 b, float amplitude, int detail, Random rng)
    {
        var points = new List<Vector3> { a, b };
        if (amplitude <= 0.0f || detail <= 0)
        {
            return points;
        }

        for (int depth = 0; depth < detail; depth++)
        {
            float levelAmplitude = amplitude * Mathf.Pow(0.5f, depth);
            var next = new List<Vector3>((points.Count * 2) - 1);
            for (int i = 0; i < points.Count - 1; i++)
            {
                next.Add(points[i]);
                next.Add(Displace(points[i], points[i + 1], levelAmplitude, rng));
            }

            next.Add(points[^1]);
            points = next;
        }

        return points;
    }

    /// <summary>The midpoint of <paramref name="a"/>-<paramref name="b"/>, nudged by a random amount
    /// within the plane perpendicular to the segment (so the nudge can't stretch or shrink it).</summary>
    private static Vector3 Displace(Vector3 a, Vector3 b, float amplitude, Random rng)
    {
        Vector3 mid = (a + b) * 0.5f;
        Vector3 segment = b - a;
        float length = segment.Length();
        if (length <= 1e-5f)
        {
            return mid;
        }

        Vector3 forward = segment / length;
        Vector3 reference = Mathf.Abs(forward.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
        Vector3 axisA = reference.Cross(forward).Normalized();
        Vector3 axisB = forward.Cross(axisA).Normalized();
        float offsetA = ((float)rng.NextDouble() - 0.5f) * 2.0f * amplitude;
        float offsetB = ((float)rng.NextDouble() - 0.5f) * 2.0f * amplitude;
        return mid + (axisA * offsetA) + (axisB * offsetB);
    }

    private static void AddPost(
        Vector3 ground,
        float size,
        float height,
        float capHeight,
        float embedDepth,
        Vector3 facingHint,
        float uvScale,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> indices)
    {
        Vector3 forward = HorizontalDirection(facingHint);
        Vector3 right = Vector3.Up.Cross(forward).Normalized();

        float halfSize = size * 0.5f;
        float halfHeight = (height + embedDepth) * 0.5f;
        Vector3 center = ground + (Vector3.Up * ((height - embedDepth) * 0.5f));
        bool hasCap = capHeight > 0.0f;
        AddBox(center, right, Vector3.Up, forward, halfSize, halfHeight, halfSize, 0.0f, uvScale, includeTop: !hasCap, vertices, normals, uvs, indices);

        if (hasCap)
        {
            Vector3 shoulder = ground + (Vector3.Up * height);
            AddPyramidCap(shoulder, right, forward, halfSize, halfSize, capHeight, uvScale, vertices, normals, uvs, indices);
        }
    }

    /// <summary>Flattens <paramref name="hint"/> onto the XZ plane, falling back to a fixed axis when it
    /// has no meaningful horizontal component (e.g. a lone, perfectly vertical edge).</summary>
    private static Vector3 HorizontalDirection(Vector3 hint)
    {
        Vector3 flat = new(hint.X, 0.0f, hint.Z);
        return flat.LengthSquared() > 1e-8f ? flat.Normalized() : Vector3.Back;
    }

    private static void AddBeam(
        Vector3 a,
        Vector3 b,
        float width,
        float thickness,
        float forwardOffset,
        float uvScale,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> indices)
    {
        Vector3 axis = b - a;
        float length = axis.Length();
        if (length <= 1e-5f)
        {
            return;
        }

        Vector3 forward = axis / length;
        Vector3 reference = Mathf.Abs(forward.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
        Vector3 right = reference.Cross(forward).Normalized();
        Vector3 up = forward.Cross(right).Normalized();
        Vector3 center = (a + b) * 0.5f;
        AddBox(center, right, up, forward, width * 0.5f, thickness * 0.5f, length * 0.5f, forwardOffset, uvScale, includeTop: true, vertices, normals, uvs, indices);
    }

    /// <summary>An oriented box, <paramref name="right"/>/<paramref name="up"/>/<paramref name="forward"/>
    /// forming a right-handed basis (<c>right.Cross(up) == forward</c>). One quad per face rather than
    /// shared corner vertices, so each face keeps its own flat normal and its own UV tile.
    /// <paramref name="forwardOffset"/> shifts the UV coordinate that runs along <paramref name="forward"/>
    /// on the 4 side faces — the caller's running length along a chain of boxes, so consecutive boxes'
    /// textures continue instead of each restarting at 0 (a visible seam). <paramref name="includeTop"/>
    /// skips the <c>+up</c> face, for a post whose flat shoulder is about to be replaced by a cap.</summary>
    private static void AddBox(
        Vector3 center,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        float halfRight,
        float halfUp,
        float halfForward,
        float forwardOffset,
        float uvScale,
        bool includeTop,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> indices)
    {
        if (includeTop)
        {
            AddQuad(center + (up * halfUp), right, forward, up, halfRight, halfForward, 0.0f, forwardOffset, uvScale, vertices, normals, uvs, indices);
        }

        AddQuad(center - (up * halfUp), forward, right, -up, halfForward, halfRight, forwardOffset, 0.0f, uvScale, vertices, normals, uvs, indices);
        AddQuad(center + (right * halfRight), forward, up, right, halfForward, halfUp, forwardOffset, 0.0f, uvScale, vertices, normals, uvs, indices);
        AddQuad(center - (right * halfRight), up, forward, -right, halfUp, halfForward, 0.0f, forwardOffset, uvScale, vertices, normals, uvs, indices);
        AddQuad(center + (forward * halfForward), up, right, forward, halfUp, halfRight, 0.0f, 0.0f, uvScale, vertices, normals, uvs, indices);
        AddQuad(center - (forward * halfForward), right, up, -forward, halfRight, halfUp, 0.0f, 0.0f, uvScale, vertices, normals, uvs, indices);
    }

    /// <summary>A 4-sided point above a box's <c>+up</c> face — a post's chiselled shoulder, sized to the
    /// same cross-section its box top would have been. Each side is its own flat-shaded triangle.</summary>
    private static void AddPyramidCap(
        Vector3 baseCenter,
        Vector3 right,
        Vector3 forward,
        float halfRight,
        float halfForward,
        float height,
        float uvScale,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> indices)
    {
        Vector3 r = right * halfRight;
        Vector3 f = forward * halfForward;
        Vector3 apex = baseCenter + (Vector3.Up * height);
        Vector3[] corners =
        [
            baseCenter - r - f,
            baseCenter + r - f,
            baseCenter + r + f,
            baseCenter - r + f,
        ];

        float baseWidth = (halfRight + halfForward) * uvScale;
        float capV = height * uvScale;

        for (int i = 0; i < 4; i++)
        {
            Vector3 p0 = corners[i];
            Vector3 p1 = corners[(i + 1) % 4];

            // Godot's front face is clockwise as seen from the front (see AddQuad): the outward normal
            // is the negation of the winding order's own right-hand-rule cross product, not a separately
            // reasoned-about direction, so this is correct for all 4 sides by the same construction.
            Vector3 normal = -(p1 - p0).Cross(apex - p0).Normalized();

            int start = vertices.Count;
            vertices.Add(p0);
            vertices.Add(p1);
            vertices.Add(apex);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            uvs.Add(new Vector2(0.0f, 0.0f));
            uvs.Add(new Vector2(baseWidth, 0.0f));
            uvs.Add(new Vector2(baseWidth * 0.5f, capV));
            indices.Add(start);
            indices.Add(start + 1);
            indices.Add(start + 2);
        }
    }

    private static void AddQuad(
        Vector3 center,
        Vector3 tangent,
        Vector3 bitangent,
        Vector3 normal,
        float halfTangent,
        float halfBitangent,
        float tangentOffset,
        float bitangentOffset,
        float uvScale,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> indices)
    {
        Vector3 t = tangent * halfTangent;
        Vector3 b = bitangent * halfBitangent;
        int start = vertices.Count;

        vertices.Add(center - t - b);
        vertices.Add(center + t - b);
        vertices.Add(center + t + b);
        vertices.Add(center - t + b);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);

        float u0 = tangentOffset * uvScale;
        float u1 = (tangentOffset + (halfTangent * 2.0f)) * uvScale;
        float v0 = bitangentOffset * uvScale;
        float v1 = (bitangentOffset + (halfBitangent * 2.0f)) * uvScale;
        uvs.Add(new Vector2(u0, v0));
        uvs.Add(new Vector2(u1, v0));
        uvs.Add(new Vector2(u1, v1));
        uvs.Add(new Vector2(u0, v1));

        indices.Add(start);
        indices.Add(start + 1);
        indices.Add(start + 2);
        indices.Add(start);
        indices.Add(start + 2);
        indices.Add(start + 3);
    }
}
