using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Accumulates a procedural function's output — geometry and references per declared
/// <see cref="ProceduralOutputSlot"/>, plus landscape paint strokes — then assembles it into a
/// <see cref="ProceduralBuildResult"/>. A model output renders through the exact same
/// <see cref="ModelAsset.Instantiate"/> path as an imported one, rather than its own parallel
/// node-building loop.
/// </summary>
public sealed class ProceduralOutputBuilder
{
    private readonly Dictionary<ProceduralOutputSlot, List<ModelSurface>> _surfaces = new();
    private readonly Dictionary<ProceduralOutputSlot, List<ModelReference>> _references = new();
    private readonly Dictionary<ProceduralOutputSlot, Transform3D> _transforms = new();
    private readonly List<ProceduralStroke> _strokes = [];

    public void AddSurface(
        ProceduralOutputSlot slot,
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

        MeshMaterial material = StandardMeshMaterial.Describe(texture: texturePath, albedo: albedoColor ?? Colors.White, twoSided: true);
        Surfaces(slot).Add(new ModelSurface(name, BuildMesh(vertices, indices, normals, uvs), material));
    }

    /// <summary>Adds a surface with a caller-supplied material, e.g. one resolved from a material slot.</summary>
    public void AddSurface(
        ProceduralOutputSlot slot,
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

        Surfaces(slot).Add(new ModelSurface(name, BuildMesh(vertices, indices, normals, uvs), material));
    }

    /// <summary>Adds a pointer to another model asset at a transform relative to <paramref name="slot"/>'s
    /// output — a scattered doodad (e.g. a fence post) rather than generated geometry.</summary>
    public void AddReference(ProceduralOutputSlot slot, string name, string path, Transform3D transform) =>
        References(slot).Add(new ModelReference(name, path, transform));

    /// <summary>Places <paramref name="slot"/>'s whole output at a transform relative to the component.
    /// Identity when never called.</summary>
    public void SetTransform(ProceduralOutputSlot slot, Transform3D transform) => _transforms[slot] = transform;

    /// <summary>Adds one landscape paint primitive: a disc when <paramref name="a"/> equals
    /// <paramref name="b"/>, a stroke along the segment otherwise. Composited with the surrounding
    /// chunk by <c>max</c>, so overlapping strokes never double up. <paramref name="color"/> only
    /// matters when the target channel carries color — see <see cref="ProceduralStroke.Value"/>.</summary>
    public void AddStroke(string channel, Vector3 a, Vector3 b, float radius, float falloff, Color? color = null) =>
        _strokes.Add(new ProceduralStroke(channel, a, b, radius, falloff, color ?? Colors.White));

    /// <summary>
    /// Assembles the accumulated output: one <see cref="ProceduralModelOutput"/> per slot in
    /// <paramref name="outputs"/> that received any geometry — in that list's order, not call order —
    /// plus the accumulated paint. A slot with nothing added to it contributes no output.
    /// </summary>
    public ProceduralBuildResult Build(IReadOnlyList<ProceduralOutputSlot> outputs, Func<ProceduralOutputSlot, string> formatId)
    {
        var models = new List<ProceduralModelOutput>();
        foreach (ProceduralOutputSlot slot in outputs)
        {
            List<ModelSurface> surfaces = _surfaces.GetValueOrDefault(slot) ?? [];
            List<ModelReference> references = _references.GetValueOrDefault(slot) ?? [];
            if (surfaces.Count == 0 && references.Count == 0)
            {
                continue;
            }

            var part = new ModelPart("", Transform3D.Identity, surfaces, references);
            var asset = new ModelAsset("", [part], formatId: formatId(slot));
            Transform3D transform = _transforms.GetValueOrDefault(slot, Transform3D.Identity);
            models.Add(new ProceduralModelOutput(slot, transform, asset));
        }

        ProceduralPaint paint = _strokes.Count == 0 ? ProceduralPaint.Empty : new ProceduralPaint(_strokes);
        return new ProceduralBuildResult(models, paint);
    }

    private List<ModelSurface> Surfaces(ProceduralOutputSlot slot) =>
        _surfaces.TryGetValue(slot, out List<ModelSurface>? list) ? list : _surfaces[slot] = [];

    private List<ModelReference> References(ProceduralOutputSlot slot) =>
        _references.TryGetValue(slot, out List<ModelReference>? list) ? list : _references[slot] = [];

    private static ArrayMesh BuildMesh(
        IReadOnlyList<Vector3> vertices,
        IReadOnlyList<int> indices,
        IReadOnlyList<Vector3>? normals,
        IReadOnlyList<Vector2>? uvs)
    {
        using var arrays = new Godot.Collections.Array();
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
        return mesh;
    }
}
