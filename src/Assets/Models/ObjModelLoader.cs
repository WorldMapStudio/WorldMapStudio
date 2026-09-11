using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>Loads a practical static subset of Wavefront OBJ from provider text.</summary>
[Subsystem(nameof(AssetSystem))]
public sealed class ObjModelLoader : IModelLoader
{
    private sealed record FaceVertex(int Position, int Uv, int Normal);

    private sealed class SurfaceBuilder
    {
        public readonly List<Vector3> Vertices = [];
        public readonly List<Vector2> Uvs = [];
        public readonly List<Vector3> Normals = [];
        public bool HasUv;
        public bool HasNormal;
    }

    public ObjModelLoader(AssetSystem assets)
    {
    }

    public float Priority => 0.0f;

    public string FormatId => ObjModelFormat.FormatId;

    public bool CanLoad(string path) => string.Equals(AssetPath.Extension(path), ".obj", StringComparison.OrdinalIgnoreCase);

    public async Task<ModelAsset?> LoadModelAsync(AssetSystem assets, string path)
    {
        string? text = await assets.ReadAssetTextAsync(path).ConfigureAwait(false);
        if (text == null)
        {
            return null;
        }

        Dictionary<string, ObjMaterial> materials = await LoadMaterialsAsync(assets, path, text).ConfigureAwait(false);
        List<Vector3> positions = [];
        List<Vector2> uvs = [];
        List<Vector3> normals = [];
        Dictionary<string, SurfaceBuilder> surfaces = new(StringComparer.OrdinalIgnoreCase);
        string currentMaterial = "";

        foreach (string rawLine in Lines(text))
        {
            string line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] parts = Split(line);
            switch (parts[0])
            {
                case "v" when parts.Length >= 4:
                    positions.Add(new Vector3(Number(parts[1]), Number(parts[2]), Number(parts[3])));
                    break;
                case "vt" when parts.Length >= 3:
                    uvs.Add(new Vector2(Number(parts[1]), 1.0f - Number(parts[2])));
                    break;
                case "vn" when parts.Length >= 4:
                    normals.Add(new Vector3(Number(parts[1]), Number(parts[2]), Number(parts[3])).Normalized());
                    break;
                case "usemtl" when parts.Length >= 2:
                    currentMaterial = parts[1];
                    break;
                case "f" when parts.Length >= 4:
                    AddFace(parts.Skip(1).Select(part => ParseFaceVertex(part, positions.Count, uvs.Count, normals.Count)).ToList(),
                        SurfaceFor(surfaces, currentMaterial), positions, uvs, normals);
                    break;
            }
        }

        var modelSurfaces = new List<ModelSurface>();
        int index = 0;
        foreach ((string name, SurfaceBuilder builder) in surfaces)
        {
            if (builder.Vertices.Count == 0)
            {
                continue;
            }

            string texturePath = materials.TryGetValue(name, out ObjMaterial? material) ? material.TexturePath : "";
            Color color = material?.Color ?? ModelAsset.FallbackColor(index);
            MeshMaterial meshMaterial = StandardMeshMaterial.Describe(texture: texturePath, albedo: color, twoSided: true);
            modelSurfaces.Add(new ModelSurface(name, BuildMesh(builder), meshMaterial));
            index++;
        }

        return modelSurfaces.Count == 0 ? null : new ModelAsset(path, modelSurfaces, formatId: FormatId);
    }

    private static SurfaceBuilder SurfaceFor(Dictionary<string, SurfaceBuilder> surfaces, string material)
    {
        string key = material.Length == 0 ? "Default" : material;
        if (!surfaces.TryGetValue(key, out SurfaceBuilder? builder))
        {
            builder = new SurfaceBuilder();
            surfaces[key] = builder;
        }

        return builder;
    }

    private static void AddFace(
        IReadOnlyList<FaceVertex> face,
        SurfaceBuilder surface,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector2> uvs,
        IReadOnlyList<Vector3> normals)
    {
        if (face.Any(vertex => vertex.Position < 0 || vertex.Position >= positions.Count))
        {
            return;
        }

        for (int i = 1; i < face.Count - 1; i++)
        {
            bool triangleHasNormal = face[0].Normal >= 0 && face[i].Normal >= 0 && face[i + 1].Normal >= 0;
            AddVertex(face[0], surface, positions, uvs, normals);
            AddVertex(face[i + 1], surface, positions, uvs, normals);
            AddVertex(face[i], surface, positions, uvs, normals);

            if (!triangleHasNormal)
            {
                int last = surface.Vertices.Count;
                Vector3 normal = (surface.Vertices[last - 2] - surface.Vertices[last - 3])
                    .Cross(surface.Vertices[last - 1] - surface.Vertices[last - 3])
                    .Normalized();
                surface.Normals[last - 3] = normal;
                surface.Normals[last - 2] = normal;
                surface.Normals[last - 1] = normal;
            }
        }
    }

    private static void AddVertex(
        FaceVertex vertex,
        SurfaceBuilder surface,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector2> uvs,
        IReadOnlyList<Vector3> normals)
    {
        surface.Vertices.Add(positions[vertex.Position]);

        if (vertex.Uv >= 0)
        {
            surface.HasUv = true;
            surface.Uvs.Add(uvs[vertex.Uv]);
        }
        else
        {
            surface.Uvs.Add(Vector2.Zero);
        }

        if (vertex.Normal >= 0)
        {
            surface.HasNormal = true;
            surface.Normals.Add(normals[vertex.Normal]);
        }
        else
        {
            surface.Normals.Add(Vector3.Up);
        }
    }

    private static ArrayMesh BuildMesh(SurfaceBuilder surface)
    {
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = surface.Vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = surface.Normals.ToArray();
        if (surface.HasUv)
        {
            arrays[(int)Mesh.ArrayType.TexUV] = surface.Uvs.ToArray();
        }

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static FaceVertex ParseFaceVertex(string value, int positionCount, int uvCount, int normalCount)
    {
        string[] parts = value.Split('/');
        return new FaceVertex(
            ResolveIndex(parts.ElementAtOrDefault(0), positionCount),
            ResolveIndex(parts.ElementAtOrDefault(1), uvCount),
            ResolveIndex(parts.ElementAtOrDefault(2), normalCount));
    }

    private static int ResolveIndex(string? value, int count)
    {
        if (string.IsNullOrWhiteSpace(value) || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
        {
            return -1;
        }

        int resolved = index < 0 ? count + index : index - 1;
        return resolved >= 0 && resolved < count ? resolved : -1;
    }

    private static async Task<Dictionary<string, ObjMaterial>> LoadMaterialsAsync(AssetSystem assets, string objPath, string objText)
    {
        var result = new Dictionary<string, ObjMaterial>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in Lines(objText))
        {
            string line = StripComment(rawLine).Trim();
            string[] parts = Split(line);
            if (parts.Length < 2 || parts[0] != "mtllib")
            {
                continue;
            }

            string materialPath = AssetPath.RelativeTo(objPath, string.Join(' ', parts.Skip(1)));
            if (await assets.ReadAssetTextAsync(materialPath).ConfigureAwait(false) is { } materialText)
            {
                foreach ((string name, ObjMaterial material) in ParseMaterialLibrary(objPath, materialPath, materialText))
                {
                    result[name] = material;
                }
            }
        }

        return result;
    }

    private static IEnumerable<KeyValuePair<string, ObjMaterial>> ParseMaterialLibrary(string objPath, string materialPath, string text)
    {
        string current = "";
        string texture = "";
        Color color = Colors.White;

        foreach (string rawLine in Lines(text))
        {
            string line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] parts = Split(line);
            if (parts[0] == "newmtl" && parts.Length >= 2)
            {
                if (current.Length > 0)
                {
                    yield return new KeyValuePair<string, ObjMaterial>(current, new ObjMaterial(texture, color));
                }

                current = parts[1];
                texture = "";
                color = Colors.White;
            }
            else if (parts[0] == "map_Kd" && parts.Length >= 2)
            {
                texture = AssetPath.RelativeTo(materialPath, string.Join(' ', parts.Skip(1)));
            }
            else if (parts[0] == "Kd" && parts.Length >= 4)
            {
                color = new Color(Number(parts[1]), Number(parts[2]), Number(parts[3]));
            }
        }

        if (current.Length > 0)
        {
            yield return new KeyValuePair<string, ObjMaterial>(current, new ObjMaterial(texture, color));
        }
    }

    private static IEnumerable<string> Lines(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static string StripComment(string line)
    {
        int index = line.IndexOf('#');
        return index < 0 ? line : line[..index];
    }

    private static string[] Split(string line) =>
        line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

    private static float Number(string value) =>
        float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private sealed record ObjMaterial(string TexturePath, Color Color);
}
