using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Accumulates a procedural function's output as plain <see cref="ModelSurface"/>s, then wraps them
/// in a <see cref="ModelAsset"/> — a procedural mesh renders through the exact same
/// <see cref="ModelAsset.Instantiate"/> path as an imported one, rather than its own parallel node-building
/// loop.
/// </summary>
public sealed class ProceduralMeshOutputBuilder
{
    private readonly List<ModelSurface> _surfaces = [];

    public void AddSurface(
        string name,
        IReadOnlyList<Vector3> vertices,
        IReadOnlyList<int> indices,
        IReadOnlyList<Vector3>? normals = null,
        IReadOnlyList<Vector2>? uvs = null,
        string texturePath = "",
        Color? albedoColor = null)
    {
        if (vertices.Count == 0 || indices.Count == 0)
        {
            return;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        if (normals != null && normals.Count == vertices.Count)
        {
            arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        }

        if (uvs != null && uvs.Count == vertices.Count)
        {
            arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        }

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        MeshMaterial material = StandardMeshMaterial.Describe(texture: texturePath, albedo: albedoColor ?? Colors.White, twoSided: true);
        _surfaces.Add(new ModelSurface(name, mesh, material));
    }

    /// <summary>Adds a surface with a caller-supplied material, e.g. one resolved from a material slot.</summary>
    public void AddSurface(
        string name,
        IReadOnlyList<Vector3> vertices,
        IReadOnlyList<int> indices,
        MeshMaterial material,
        IReadOnlyList<Vector3>? normals = null,
        IReadOnlyList<Vector2>? uvs = null)
    {
        if (vertices.Count == 0 || indices.Count == 0)
        {
            return;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        if (normals != null && normals.Count == vertices.Count)
        {
            arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        }

        if (uvs != null && uvs.Count == vertices.Count)
        {
            arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        }

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _surfaces.Add(new ModelSurface(name, mesh, material));
    }

    public ModelAsset Build(string formatId) => new("", _surfaces, formatId: formatId);
}
