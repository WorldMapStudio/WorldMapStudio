using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

public sealed record ProceduralMeshSurface(
    string Name,
    ArrayMesh Mesh,
    string TexturePath,
    Color AlbedoColor)
{
    public Aabb LocalBounds => Mesh.GetAabb();
}

public sealed class ProceduralMeshOutput
{
    public ProceduralMeshOutput(IEnumerable<ProceduralMeshSurface> surfaces)
    {
        Surfaces = surfaces.Where(surface => surface.Mesh.GetSurfaceCount() > 0).ToList();
        LocalBounds = ModelAsset.CombineBounds(Surfaces.Select(surface => surface.LocalBounds));
    }

    public IReadOnlyList<ProceduralMeshSurface> Surfaces { get; }

    public Aabb LocalBounds { get; }
}

public sealed class ProceduralMeshOutputBuilder
{
    private readonly List<ProceduralMeshSurface> _surfaces = [];

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
        _surfaces.Add(new ProceduralMeshSurface(name, mesh, texturePath, albedoColor ?? Colors.White));
    }

    public ProceduralMeshOutput Build() => new(_surfaces);
}
