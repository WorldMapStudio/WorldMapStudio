using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

[Subsystem(nameof(ProceduralSystem))]
public sealed class TubeNetworkMeshFunction : IProceduralFunction
{
    public static readonly MeshParameter Radius =
        MeshParameter.Float("radius", "Radius", 0.25f, 0.01f, 128.0f, "Tube radius in local units.");

    public static readonly MeshParameter Segments =
        MeshParameter.Int("segments", "Segments", 8, 3, 64, "Radial segment count.");

    public static readonly MeshParameter UvScale =
        MeshParameter.Float("uv_scale", "UV scale", 1.0f, 0.01f, 1024.0f, "Texture repeats per local unit.");

    public static readonly MeshMaterialSlot Surface = new("surface", "Surface", "Tube material.");

    public static readonly ProceduralOutputSlot Output = new("mesh", "Mesh", [], [Surface]);

    public string Id => "builtin.mesh.tube_network";

    public string DisplayName => "Tube Network";

    public string Description => "Turns every network edge into a textured round tube.";

    public int Version => 2;

    public bool AllowsMultipleGraphs => true;

    public bool AllowsBranching => true;

    public float Priority => 0f;

    public IReadOnlyList<MeshParameter> Parameters { get; } =
        MeshParameter.List(Radius, Segments, UvScale);

    public IReadOnlyList<ProceduralOutputSlot> Outputs { get; } = [Output];

    public TubeNetworkMeshFunction(ProceduralSystem system)
    {
    }

    public void Build(in ProceduralBuildContext context, ProceduralOutputBuilder output)
    {
        float radius = Mathf.Max(0.001f, context.Float(Radius));
        int segments = Mathf.Clamp(context.Int(Segments), 3, 64);
        float uvScale = Mathf.Max(0.001f, context.Float(UvScale));

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();

        foreach (NetworkEdge edge in context.Network.Edges)
        {
            NetworkVertex? a = context.Network.Vertex(edge.A);
            NetworkVertex? b = context.Network.Vertex(edge.B);
            if (a == null || b == null)
            {
                continue;
            }

            AddTube(a.Position, b.Position, radius, segments, uvScale, vertices, normals, uvs, indices);
        }

        output.AddSurface(Output, "Tubes", vertices, indices, context.Material(Output, Surface), normals, uvs);
    }

    private static void AddTube(
        Vector3 a,
        Vector3 b,
        float radius,
        int segments,
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
        int start = vertices.Count;

        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.Tau * i / segments;
            Vector3 normal = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)).Normalized();
            vertices.Add(a + normal * radius);
            vertices.Add(b + normal * radius);
            normals.Add(normal);
            normals.Add(normal);
            float u = i / (float)segments;
            uvs.Add(new Vector2(u, 0.0f));
            uvs.Add(new Vector2(u, length * uvScale));
        }

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int a0 = start + i * 2;
            int b0 = a0 + 1;
            int a1 = start + next * 2;
            int b1 = a1 + 1;

            indices.Add(a0);
            indices.Add(a1);
            indices.Add(b0);
            indices.Add(a1);
            indices.Add(b1);
            indices.Add(b0);
        }
    }
}
