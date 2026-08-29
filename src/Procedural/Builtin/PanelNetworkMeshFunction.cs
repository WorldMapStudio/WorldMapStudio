using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Turns every authored network face into a flat, textured panel — a roof, wall or floor plate. Fan-
/// triangulates each face's n-gon loop from its first vertex, which is exact for any planar convex
/// face and a reasonable approximation otherwise (the common authored shapes: triangles and quads).
/// Demonstrates that <see cref="VertexNetwork.Faces"/> reaches <see cref="Build"/> the same way
/// <see cref="TubeNetworkMeshFunction"/> demonstrates edges.
/// </summary>
[Subsystem(nameof(ProceduralSystem))]
public sealed class PanelNetworkMeshFunction : IProceduralFunction
{
    public static readonly MeshParameter UvScale =
        MeshParameter.Float("uv_scale", "UV scale", 1.0f, 0.01f, 1024.0f, "Texture repeats per local unit.");

    public static readonly MeshMaterialSlot Surface = new("surface", "Surface", "Panel material.");

    public static readonly ProceduralOutputSlot Output = new("mesh", "Mesh", [], [Surface]);

    public string Id => "builtin.mesh.panel_network";

    public string DisplayName => "Panel Network";

    public string Description => "Turns every network face into a flat, textured panel.";

    public int Version => 1;

    public NetworkCapabilities Capabilities => new(AllowsFaces: true);

    public float Priority => 0f;

    public IReadOnlyList<MeshParameter> Parameters { get; } = MeshParameter.List(UvScale);

    public IReadOnlyList<ProceduralOutputSlot> Outputs { get; } = [Output];

    public PanelNetworkMeshFunction(ProceduralSystem system)
    {
    }

    public void Build(in ProceduralBuildContext context, ProceduralOutputBuilder output)
    {
        float uvScale = Mathf.Max(0.001f, context.Float(UvScale));

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();

        foreach (NetworkFace face in context.Network.Faces)
        {
            AddPanel(context.Network, face, uvScale, vertices, normals, uvs, indices);
        }

        output.AddSurface(Output, "Panels", vertices, indices, context.Material(Output, Surface), normals, uvs);
    }

    private static void AddPanel(
        VertexNetwork network,
        NetworkFace face,
        float uvScale,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> indices)
    {
        IReadOnlyList<int> loop = face.Vertices;
        if (loop.Count < 3)
        {
            return;
        }

        var positions = new Vector3[loop.Count];
        for (int i = 0; i < loop.Count; i++)
        {
            positions[i] = network.Vertex(loop[i])?.Position ?? Vector3.Zero;
        }

        Vector3 normal = FaceNormal(positions);
        if (normal.LengthSquared() <= 1e-10f)
        {
            return;
        }

        Vector3 reference = Mathf.Abs(normal.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
        Vector3 uAxis = reference.Cross(normal).Normalized();
        Vector3 vAxis = normal.Cross(uAxis).Normalized();

        int start = vertices.Count;
        foreach (Vector3 position in positions)
        {
            vertices.Add(position);
            normals.Add(normal);
            uvs.Add(new Vector2(position.Dot(uAxis) * uvScale, position.Dot(vAxis) * uvScale));
        }

        for (int i = 1; i < loop.Count - 1; i++)
        {
            indices.Add(start);
            indices.Add(start + i);
            indices.Add(start + i + 1);
        }
    }

    /// <summary>Newell's method — robust for a near-planar n-gon, unlike a three-point cross product.</summary>
    private static Vector3 FaceNormal(IReadOnlyList<Vector3> loop)
    {
        Vector3 normal = Vector3.Zero;
        for (int i = 0; i < loop.Count; i++)
        {
            Vector3 current = loop[i];
            Vector3 next = loop[(i + 1) % loop.Count];
            normal.X += (current.Y - next.Y) * (current.Z + next.Z);
            normal.Y += (current.Z - next.Z) * (current.X + next.X);
            normal.Z += (current.X - next.X) * (current.Y + next.Y);
        }

        return normal.LengthSquared() <= 1e-10f ? normal : normal.Normalized();
    }
}
