using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Turns a <see cref="VertexNetwork"/> into a post-and-rail fence: a post at every authored vertex,
/// plus evenly spaced posts filled in along any span longer than <see cref="PostSpacing"/>, with
/// straight rails welded between them. Vertex height is authored per point (<see cref="SnapToTerrainOnPlace"/>
/// snaps a new point onto the terrain under the cursor, keeping its real height) rather than resampled
/// at build time, so a rail's undulation across a hillside is only as accurate as its posts are close
/// together — the same tradeoff a real fence makes.
/// </summary>
[Subsystem(nameof(ProceduralSystem))]
public sealed class FenceNetworkMeshFunction : IProceduralFunction
{
    public static readonly MeshParameter PostSpacing =
        MeshParameter.Float("post_spacing", "Post spacing", 3.0f, 0.5f, 64.0f, "Target spacing between automatically filled-in posts along a long span.");

    public static readonly MeshParameter PostSize =
        MeshParameter.Float("post_size", "Post size", 0.18f, 0.02f, 4.0f, "Width and depth of each post's square cross-section.");

    public static readonly MeshParameter PostHeight =
        MeshParameter.Float("post_height", "Post height", 1.1f, 0.05f, 16.0f, "Height of a post above its authored ground point.");

    public static readonly MeshParameter EmbedDepth =
        MeshParameter.Float("embed_depth", "Embed depth", 0.4f, 0.0f, 8.0f, "How far a post extends below its authored ground point.");

    public static readonly MeshParameter RailCount =
        MeshParameter.Int("rail_count", "Rail count", 2, 1, 6, "Number of horizontal rails.");

    public static readonly MeshParameter RailTopOffset =
        MeshParameter.Float("rail_top_offset", "Rail top offset", 0.1f, 0.0f, 8.0f, "Distance from a post's top down to the top rail.");

    public static readonly MeshParameter RailGap =
        MeshParameter.Float("rail_gap", "Rail gap", 0.35f, 0.02f, 8.0f, "Vertical spacing between consecutive rails.");

    public static readonly MeshParameter RailWidth =
        MeshParameter.Float("rail_width", "Rail width", 0.14f, 0.01f, 4.0f, "Rail plank width, across the fence line.");

    public static readonly MeshParameter RailThickness =
        MeshParameter.Float("rail_thickness", "Rail thickness", 0.06f, 0.01f, 4.0f, "Rail plank thickness.");

    public static readonly MeshParameter UvScale =
        MeshParameter.Float("uv_scale", "UV scale", 1.0f, 0.01f, 1024.0f, "Texture repeats per local unit.");

    public static readonly MeshMaterialSlot Surface = new("surface", "Surface", "Fence material — rails and posts share it.");

    public static readonly ProceduralOutputSlot Output = new("mesh", "Mesh", [], [Surface]);

    public string Id => "builtin.mesh.fence_network";

    public string DisplayName => "Fence Network";

    public string Description => "Turns a network into a post-and-rail fence: posts at every vertex, welded to straight rails between them.";

    public int Version => 1;

    public float Priority => 0f;

    /// <summary>New points snap onto the terrain under the cursor — see the class comment.</summary>
    public bool SnapToTerrainOnPlace => true;

    public IReadOnlyList<MeshParameter> Parameters { get; } = MeshParameter.List(
        PostSpacing, PostSize, PostHeight, EmbedDepth, RailCount, RailTopOffset, RailGap, RailWidth, RailThickness, UvScale);

    public IReadOnlyList<ProceduralOutputSlot> Outputs { get; } = [Output];

    public FenceNetworkMeshFunction(ProceduralSystem system)
    {
    }

    public void Build(in ProceduralBuildContext context, ProceduralOutputBuilder output)
    {
        float postSpacing = Mathf.Max(0.1f, context.Float(PostSpacing));
        float postSize = Mathf.Max(0.01f, context.Float(PostSize));
        float postHeight = Mathf.Max(0.01f, context.Float(PostHeight));
        float embedDepth = Mathf.Max(0.0f, context.Float(EmbedDepth));
        int railCount = Mathf.Clamp(context.Int(RailCount), 1, 6);
        float railTopOffset = Mathf.Max(0.0f, context.Float(RailTopOffset));
        float railGap = Mathf.Max(0.01f, context.Float(RailGap));
        float railWidth = Mathf.Max(0.01f, context.Float(RailWidth));
        float railThickness = Mathf.Max(0.01f, context.Float(RailThickness));
        float uvScale = Mathf.Max(0.001f, context.Float(UvScale));

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        var postedVertices = new HashSet<int>();

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
                AddPost(a.Position, postSize, postHeight, embedDepth, uvScale, vertices, normals, uvs, indices);
            }

            if (postedVertices.Add(b.Id))
            {
                AddPost(b.Position, postSize, postHeight, embedDepth, uvScale, vertices, normals, uvs, indices);
            }

            List<Vector3> waypoints = Subdivide(a.Position, b.Position, postSpacing);
            for (int i = 1; i < waypoints.Count - 1; i++)
            {
                AddPost(waypoints[i], postSize, postHeight, embedDepth, uvScale, vertices, normals, uvs, indices);
            }

            for (int i = 0; i < waypoints.Count - 1; i++)
            {
                for (int rail = 0; rail < railCount; rail++)
                {
                    Vector3 railUp = Vector3.Up * (postHeight - railTopOffset - (rail * railGap));
                    AddBeam(waypoints[i] + railUp, waypoints[i + 1] + railUp, railWidth, railThickness, uvScale, vertices, normals, uvs, indices);
                }
            }
        }

        output.AddSurface(Output, "Fence", vertices, indices, context.Material(Output, Surface), normals, uvs);
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

    private static void AddPost(
        Vector3 ground,
        float size,
        float height,
        float embedDepth,
        float uvScale,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> indices)
    {
        float halfSize = size * 0.5f;
        float halfHeight = (height + embedDepth) * 0.5f;
        Vector3 center = ground + Vector3.Up * ((height - embedDepth) * 0.5f);
        AddBox(center, Vector3.Right, Vector3.Up, Vector3.Back, halfSize, halfHeight, halfSize, uvScale, vertices, normals, uvs, indices);
    }

    private static void AddBeam(
        Vector3 a,
        Vector3 b,
        float width,
        float thickness,
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
        AddBox(center, right, up, forward, width * 0.5f, thickness * 0.5f, length * 0.5f, uvScale, vertices, normals, uvs, indices);
    }

    /// <summary>An oriented box, <paramref name="right"/>/<paramref name="up"/>/<paramref name="forward"/>
    /// forming a right-handed basis (<c>right.Cross(up) == forward</c>). One quad per face rather than
    /// shared corner vertices, so each face keeps its own flat normal and its own 0..1 UV tile.</summary>
    private static void AddBox(
        Vector3 center,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        float halfRight,
        float halfUp,
        float halfForward,
        float uvScale,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> indices)
    {
        AddQuad(center + (up * halfUp), right, forward, up, halfRight, halfForward, uvScale, vertices, normals, uvs, indices);
        AddQuad(center - (up * halfUp), forward, right, -up, halfForward, halfRight, uvScale, vertices, normals, uvs, indices);
        AddQuad(center + (right * halfRight), forward, up, right, halfForward, halfUp, uvScale, vertices, normals, uvs, indices);
        AddQuad(center - (right * halfRight), up, forward, -right, halfUp, halfForward, uvScale, vertices, normals, uvs, indices);
        AddQuad(center + (forward * halfForward), up, right, forward, halfUp, halfRight, uvScale, vertices, normals, uvs, indices);
        AddQuad(center - (forward * halfForward), right, up, -forward, halfRight, halfUp, uvScale, vertices, normals, uvs, indices);
    }

    /// <summary>One box face. <paramref name="tangent"/>/<paramref name="bitangent"/> must satisfy
    /// <c>tangent.Cross(bitangent) == -normal</c> — Godot's front face is clockwise as seen from the
    /// front (the opposite of the OpenGL habit), so a face visible from <paramref name="normal"/>'s
    /// direction needs its winding built from that inverted relationship, not the plain right-hand
    /// rule. See <c>LandscapeChunkMesh.BuildIndices</c> for the same rule pinned against a terrain grid.</summary>
    private static void AddQuad(
        Vector3 center,
        Vector3 tangent,
        Vector3 bitangent,
        Vector3 normal,
        float halfTangent,
        float halfBitangent,
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

        float u = halfTangent * 2.0f * uvScale;
        float v = halfBitangent * 2.0f * uvScale;
        uvs.Add(new Vector2(0.0f, 0.0f));
        uvs.Add(new Vector2(u, 0.0f));
        uvs.Add(new Vector2(u, v));
        uvs.Add(new Vector2(0.0f, v));

        indices.Add(start);
        indices.Add(start + 1);
        indices.Add(start + 2);
        indices.Add(start);
        indices.Add(start + 2);
        indices.Add(start + 3);
    }
}
