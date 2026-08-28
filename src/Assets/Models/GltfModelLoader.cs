using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>Loads static glTF 2.0 meshes from provider bytes, including provider-relative buffers and textures.</summary>
[Subsystem(nameof(AssetSystem))]
public sealed class GltfModelLoader : IModelLoader
{
    private const uint GlbMagic = 0x46546C67;
    private const uint GlbJsonChunk = 0x4E4F534A;
    private const uint GlbBinaryChunk = 0x004E4942;

    private sealed record BufferView(int Buffer, int Offset, int Length, int Stride);
    private sealed record Accessor(int BufferView, int Offset, int ComponentType, int Count, string Type, bool Normalized);
    private sealed record GltfSource(JsonDocument Json, List<byte[]> Buffers, byte[]? BinaryChunk);
    private sealed record GltfMaterial(string TexturePath, Color Color);

    public GltfModelLoader(AssetSystem assets)
    {
    }

    public float Priority => 0.0f;

    public bool CanLoad(string path)
    {
        string extension = AssetPath.Extension(path);
        return string.Equals(extension, ".gltf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".glb", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ModelAsset?> LoadModelAsync(AssetSystem assets, string path)
    {
        GltfSource? source = await LoadSourceAsync(assets, path).ConfigureAwait(false);
        if (source == null)
        {
            return null;
        }

        using JsonDocument json = source.Json;
        JsonElement root = json.RootElement;
        List<BufferView> views = ReadBufferViews(root);
        List<Accessor> accessors = ReadAccessors(root);
        List<string> images = ReadImages(root, path);
        List<string> textureImages = ReadTextures(root, images);
        List<GltfMaterial> materials = ReadMaterials(root, textureImages);

        if (!root.TryGetProperty("meshes", out JsonElement meshes) || meshes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var surfaces = new List<ModelSurface>();
        for (int meshIndex = 0; meshIndex < meshes.GetArrayLength(); meshIndex++)
        {
            JsonElement mesh = meshes[meshIndex];
            string meshName = String(mesh, "name", $"Mesh{meshIndex}");
            if (!mesh.TryGetProperty("primitives", out JsonElement primitives) || primitives.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            for (int primitiveIndex = 0; primitiveIndex < primitives.GetArrayLength(); primitiveIndex++)
            {
                JsonElement primitive = primitives[primitiveIndex];
                if (Int(primitive, "mode", 4) != 4 ||
                    !primitive.TryGetProperty("attributes", out JsonElement attributes) ||
                    !attributes.TryGetProperty("POSITION", out JsonElement positionAccessor))
                {
                    continue;
                }

                Vector3[] positions = ReadVector3(source.Buffers, views, accessors, positionAccessor.GetInt32());
                Vector3[] normals = attributes.TryGetProperty("NORMAL", out JsonElement normalAccessor)
                    ? ReadVector3(source.Buffers, views, accessors, normalAccessor.GetInt32())
                    : [];
                Vector2[] uvs = attributes.TryGetProperty("TEXCOORD_0", out JsonElement uvAccessor)
                    ? ReadVector2(source.Buffers, views, accessors, uvAccessor.GetInt32(), flipY: true)
                    : [];
                int[] indices = primitive.TryGetProperty("indices", out JsonElement indexAccessor)
                    ? ReadIndices(source.Buffers, views, accessors, indexAccessor.GetInt32())
                    : Enumerable.Range(0, positions.Length).ToArray();

                if (positions.Length == 0 || indices.Length < 3)
                {
                    continue;
                }

                indices = RewindTriangles(indices);
                if (normals.Length != positions.Length)
                {
                    normals = GenerateNormals(positions, indices);
                }

                GltfMaterial material = MaterialFor(materials, Int(primitive, "material", -1), surfaces.Count);
                surfaces.Add(new ModelSurface($"{meshName}.{primitiveIndex}", BuildMesh(positions, normals, uvs, indices), material.TexturePath, material.Color));
            }
        }

        return surfaces.Count == 0 ? null : new ModelAsset(path, surfaces);
    }

    private static async Task<GltfSource?> LoadSourceAsync(AssetSystem assets, string path)
    {
        byte[]? bytes = await assets.ReadAssetBytesAsync(path).ConfigureAwait(false);
        if (bytes == null)
        {
            return null;
        }

        if (string.Equals(AssetPath.Extension(path), ".glb", StringComparison.OrdinalIgnoreCase))
        {
            return ParseGlb(bytes);
        }

        var json = JsonDocument.Parse(bytes);
        var source = new GltfSource(json, [], null);
        await LoadBuffersAsync(assets, path, source).ConfigureAwait(false);
        return source;
    }

    private static GltfSource? ParseGlb(byte[] bytes)
    {
        if (bytes.Length < 20 ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)) != GlbMagic ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) != 2)
        {
            return null;
        }

        int offset = 12;
        JsonDocument? json = null;
        byte[]? binary = null;

        while (offset + 8 <= bytes.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            offset += 8;
            if (length < 0 || offset + length > bytes.Length)
            {
                break;
            }

            byte[] chunk = bytes.AsSpan(offset, length).ToArray();
            if (type == GlbJsonChunk)
            {
                json = JsonDocument.Parse(chunk);
            }
            else if (type == GlbBinaryChunk)
            {
                binary = chunk;
            }

            offset += length;
        }

        if (json == null)
        {
            return null;
        }

        var source = new GltfSource(json, [], binary);
        if (binary != null)
        {
            source.Buffers.Add(binary);
        }

        return source;
    }

    private static async Task LoadBuffersAsync(AssetSystem assets, string path, GltfSource source)
    {
        JsonElement root = source.Json.RootElement;
        if (!root.TryGetProperty("buffers", out JsonElement buffers) || buffers.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        for (int i = 0; i < buffers.GetArrayLength(); i++)
        {
            JsonElement buffer = buffers[i];
            string uri = String(buffer, "uri", "");
            if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                source.Buffers.Add(ParseDataUri(uri) ?? []);
                continue;
            }

            byte[]? bytes = uri.Length == 0
                ? source.BinaryChunk
                : await assets.ReadAssetBytesAsync(AssetPath.RelativeTo(path, uri)).ConfigureAwait(false);
            source.Buffers.Add(bytes ?? []);
        }
    }

    private static List<BufferView> ReadBufferViews(JsonElement root)
    {
        var result = new List<BufferView>();
        if (!root.TryGetProperty("bufferViews", out JsonElement views) || views.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement view in views.EnumerateArray())
        {
            result.Add(new BufferView(
                Int(view, "buffer", 0),
                Int(view, "byteOffset", 0),
                Int(view, "byteLength", 0),
                Int(view, "byteStride", 0)));
        }

        return result;
    }

    private static List<Accessor> ReadAccessors(JsonElement root)
    {
        var result = new List<Accessor>();
        if (!root.TryGetProperty("accessors", out JsonElement accessors) || accessors.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement accessor in accessors.EnumerateArray())
        {
            result.Add(new Accessor(
                Int(accessor, "bufferView", -1),
                Int(accessor, "byteOffset", 0),
                Int(accessor, "componentType", 0),
                Int(accessor, "count", 0),
                String(accessor, "type", ""),
                Bool(accessor, "normalized", false)));
        }

        return result;
    }

    private static List<string> ReadImages(JsonElement root, string path)
    {
        var result = new List<string>();
        if (!root.TryGetProperty("images", out JsonElement images) || images.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement image in images.EnumerateArray())
        {
            string uri = String(image, "uri", "");
            result.Add(uri.Length == 0 || uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? ""
                : AssetPath.RelativeTo(path, uri));
        }

        return result;
    }

    private static List<string> ReadTextures(JsonElement root, IReadOnlyList<string> images)
    {
        var result = new List<string>();
        if (!root.TryGetProperty("textures", out JsonElement textures) || textures.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement texture in textures.EnumerateArray())
        {
            int source = Int(texture, "source", -1);
            result.Add(source >= 0 && source < images.Count ? images[source] : "");
        }

        return result;
    }

    private static List<GltfMaterial> ReadMaterials(JsonElement root, IReadOnlyList<string> textureImages)
    {
        var result = new List<GltfMaterial>();
        if (!root.TryGetProperty("materials", out JsonElement materials) || materials.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement material in materials.EnumerateArray())
        {
            JsonElement pbr = material.TryGetProperty("pbrMetallicRoughness", out JsonElement value) ? value : default;
            string texture = "";
            if (pbr.ValueKind == JsonValueKind.Object &&
                pbr.TryGetProperty("baseColorTexture", out JsonElement textureRef))
            {
                int textureIndex = Int(textureRef, "index", -1);
                texture = textureIndex >= 0 && textureIndex < textureImages.Count ? textureImages[textureIndex] : "";
            }

            Color color = Colors.White;
            if (pbr.ValueKind == JsonValueKind.Object &&
                pbr.TryGetProperty("baseColorFactor", out JsonElement colorArray) &&
                colorArray.ValueKind == JsonValueKind.Array &&
                colorArray.GetArrayLength() >= 3)
            {
                color = new Color(Number(colorArray[0]), Number(colorArray[1]), Number(colorArray[2]), colorArray.GetArrayLength() >= 4 ? Number(colorArray[3]) : 1.0f);
            }

            result.Add(new GltfMaterial(texture, color));
        }

        return result;
    }

    private static Vector3[] ReadVector3(IReadOnlyList<byte[]> buffers, IReadOnlyList<BufferView> views, IReadOnlyList<Accessor> accessors, int accessorIndex)
    {
        Accessor accessor = accessors[accessorIndex];
        if (accessor.Type != "VEC3")
        {
            return [];
        }

        var values = new Vector3[accessor.Count];
        for (int i = 0; i < values.Length; i++)
        {
            ReadOnlySpan<byte> item = ItemBytes(buffers, views, accessor, i, 12);
            values[i] = new Vector3(Float(item, 0), Float(item, 4), Float(item, 8));
        }

        return values;
    }

    private static Vector2[] ReadVector2(IReadOnlyList<byte[]> buffers, IReadOnlyList<BufferView> views, IReadOnlyList<Accessor> accessors, int accessorIndex, bool flipY)
    {
        Accessor accessor = accessors[accessorIndex];
        if (accessor.Type != "VEC2")
        {
            return [];
        }

        var values = new Vector2[accessor.Count];
        for (int i = 0; i < values.Length; i++)
        {
            ReadOnlySpan<byte> item = ItemBytes(buffers, views, accessor, i, 8);
            float y = Float(item, 4);
            values[i] = new Vector2(Float(item, 0), flipY ? 1.0f - y : y);
        }

        return values;
    }

    private static int[] ReadIndices(IReadOnlyList<byte[]> buffers, IReadOnlyList<BufferView> views, IReadOnlyList<Accessor> accessors, int accessorIndex)
    {
        Accessor accessor = accessors[accessorIndex];
        var values = new int[accessor.Count];
        int itemSize = ComponentSize(accessor.ComponentType);
        for (int i = 0; i < values.Length; i++)
        {
            ReadOnlySpan<byte> item = ItemBytes(buffers, views, accessor, i, itemSize);
            values[i] = accessor.ComponentType switch
            {
                5121 when item.Length >= 1 => item[0],
                5123 when item.Length >= 2 => BinaryPrimitives.ReadUInt16LittleEndian(item),
                5125 when item.Length >= 4 => unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(item)),
                _ => 0,
            };
        }

        return values;
    }

    private static ReadOnlySpan<byte> ItemBytes(IReadOnlyList<byte[]> buffers, IReadOnlyList<BufferView> views, Accessor accessor, int index, int packedSize)
    {
        if (accessor.BufferView < 0 || accessor.BufferView >= views.Count)
        {
            return [];
        }

        BufferView view = views[accessor.BufferView];
        if (view.Buffer < 0 || view.Buffer >= buffers.Count)
        {
            return [];
        }

        int stride = view.Stride > 0 ? view.Stride : packedSize;
        int start = view.Offset + accessor.Offset + (index * stride);
        byte[] buffer = buffers[view.Buffer];
        return start >= 0 && start + packedSize <= buffer.Length ? buffer.AsSpan(start, packedSize) : [];
    }

    private static ArrayMesh BuildMesh(Vector3[] positions, Vector3[] normals, Vector2[] uvs, int[] indices)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        if (uvs.Length == positions.Length)
        {
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        }

        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static int[] RewindTriangles(int[] indices)
    {
        int[] result = (int[])indices.Clone();
        for (int i = 0; i + 2 < result.Length; i += 3)
        {
            (result[i + 1], result[i + 2]) = (result[i + 2], result[i + 1]);
        }

        return result;
    }

    private static Vector3[] GenerateNormals(Vector3[] positions, int[] indices)
    {
        var normals = new Vector3[positions.Length];
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int a = indices[i];
            int b = indices[i + 1];
            int c = indices[i + 2];
            if (a < 0 || b < 0 || c < 0 || a >= positions.Length || b >= positions.Length || c >= positions.Length)
            {
                continue;
            }

            Vector3 normal = (positions[b] - positions[a]).Cross(positions[c] - positions[a]).Normalized();
            normals[a] += normal;
            normals[b] += normal;
            normals[c] += normal;
        }

        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i].LengthSquared() > 0.0001f ? normals[i].Normalized() : Vector3.Up;
        }

        return normals;
    }

    private static GltfMaterial MaterialFor(IReadOnlyList<GltfMaterial> materials, int index, int fallbackIndex) =>
        index >= 0 && index < materials.Count
            ? materials[index]
            : new GltfMaterial("", ModelAsset.FallbackColor(fallbackIndex));

    private static byte[]? ParseDataUri(string uri)
    {
        int comma = uri.IndexOf(',');
        if (comma < 0)
        {
            return null;
        }

        string header = uri[..comma];
        string data = uri[(comma + 1)..];
        return header.Contains(";base64", StringComparison.OrdinalIgnoreCase)
            ? Convert.FromBase64String(data)
            : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(data));
    }

    private static int ComponentSize(int componentType) => componentType switch
    {
        5120 or 5121 => 1,
        5122 or 5123 => 2,
        5125 or 5126 => 4,
        _ => 4,
    };

    private static float Float(ReadOnlySpan<byte> bytes, int offset) =>
        bytes.Length >= offset + 4 ? BinaryPrimitives.ReadSingleLittleEndian(bytes[offset..]) : 0.0f;

    private static float Number(JsonElement element) => element.GetSingle();

    private static int Int(JsonElement element, string name, int fallback) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : fallback;

    private static string String(JsonElement element, string name, string fallback) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static bool Bool(JsonElement element, string name, bool fallback) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
}
