using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Turns a chunk's output into something the viewport can draw: a CPU-built mesh and a splatting
/// material.
///
/// The editor's renderer is deliberately <b>not</b> bound by the map's texture limit. That limit
/// constrains the <em>resolved output</em>, because the export target says so; how many samplers the
/// preview happens to use is our business. Slots therefore go into texture arrays with a uniform
/// count, and no shader variant is needed per limit.
/// </summary>
public static class LandscapeChunkMesh
{
    /// <summary>Placeholder texture size used until materials load real images.</summary>
    private const int PlaceholderSize = 64;

    /// <summary>How many times a slot texture tiles across one chunk.</summary>
    private const float TextureTiling = 8.0f;

    private static Shader? _shader;

    /// <summary>Builds the chunk's surface mesh from its heightmap, in chunk-local space.</summary>
    public static ArrayMesh BuildMesh(LandscapeChunkOutput output, float chunkSize)
    {
        int resolution = output.HeightResolution;
        int quads = resolution - 1;
        float step = chunkSize / quads;

        var vertices = new Vector3[resolution * resolution];
        var normals = new Vector3[resolution * resolution];
        var uvs = new Vector2[resolution * resolution];

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int index = (y * resolution) + x;
                vertices[index] = new Vector3(x * step, output.HeightAt(x, y), y * step);
                uvs[index] = new Vector2((float)x / quads, (float)y / quads);
                normals[index] = NormalAt(output, x, y, step);
            }
        }

        int[] indices = BuildIndices(resolution);

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>
    /// The triangle indices of a chunk grid, wound so the surface faces up.
    ///
    /// <b>Godot's front face is the one whose vertices are clockwise as seen from the front</b>, the
    /// opposite of the OpenGL habit. Getting this backwards produces a mesh that builds, reports
    /// upward normals, and is invisible from above because every triangle is culled — so the winding
    /// is pulled out here where a test can pin it.
    ///
    /// Separated from <see cref="BuildMesh"/> so it can be checked without a live engine.
    /// </summary>
    public static int[] BuildIndices(int resolution)
    {
        int quads = resolution - 1;
        var indices = new int[quads * quads * 6];
        int next = 0;

        for (int y = 0; y < quads; y++)
        {
            for (int x = 0; x < quads; x++)
            {
                int topLeft = (y * resolution) + x;
                int topRight = topLeft + 1;
                int bottomLeft = topLeft + resolution;
                int bottomRight = bottomLeft + 1;

                indices[next++] = topLeft;
                indices[next++] = topRight;
                indices[next++] = bottomLeft;

                indices[next++] = topRight;
                indices[next++] = bottomRight;
                indices[next++] = bottomLeft;
            }
        }

        return indices;
    }

    /// <summary>Whether chunk borders are drawn on the terrain. Set from the view settings.</summary>
    public static bool ShowChunkEdges { get; set; } = true;

    /// <summary>Builds the splatting material for a chunk's slots.</summary>
    public static ShaderMaterial BuildMaterial(LandscapeChunkOutput output, AssetSystem assets)
    {
        var material = new ShaderMaterial { Shader = SplatShader() };

        var albedos = new Godot.Collections.Array<Image>();
        var alphas = new Godot.Collections.Array<Image>();

        for (int i = 0; i < output.Layers.Count; i++)
        {
            LandscapeChunkLayer layer = output.Layers[i];
            albedos.Add(LoadAlbedo(layer.Material, i, assets));

            // Slot 0 is the opaque base and has no alpha; the array holds one image per alpha slot.
            if (layer.Alpha != null)
            {
                alphas.Add(AlphaImage(layer.Alpha, output.AlphaResolution));
            }
        }

        // A chunk can resolve to no slots at all: nothing claimed a base and the map has no fallback
        // material, which is exactly the state a landscape starts in before anything is authored.
        // That is a reported problem, not a crash — render the placeholder so the chunk is visibly
        // unassigned rather than absent.
        if (albedos.Count == 0)
        {
            albedos.Add(Placeholder(0));
        }

        // A sampler2DArray needs at least one layer even when nothing composites over the base.
        if (alphas.Count == 0)
        {
            alphas.Add(Image.CreateEmpty(1, 1, false, Image.Format.R8));
        }

        var albedoArray = new Texture2DArray();
        albedoArray.CreateFromImages(albedos);
        var alphaArray = new Texture2DArray();
        alphaArray.CreateFromImages(alphas);

        material.SetShaderParameter("slot_albedo", albedoArray);
        material.SetShaderParameter("slot_alpha", alphaArray);
        material.SetShaderParameter("slot_count", Mathf.Max(1, output.Layers.Count));
        material.SetShaderParameter("tiling", TextureTiling);
        material.SetShaderParameter("show_chunk_edges", ShowChunkEdges);
        return material;
    }

    // Central difference over the heightmap, clamped at the edges. Edge normals will disagree with
    // the neighbour's until chunks can sample across the border; harmless while heights are flat.
    private static Vector3 NormalAt(LandscapeChunkOutput output, int x, int y, float step)
    {
        int last = output.HeightResolution - 1;
        float left = output.HeightAt(Mathf.Max(x - 1, 0), y);
        float right = output.HeightAt(Mathf.Min(x + 1, last), y);
        float back = output.HeightAt(x, Mathf.Max(y - 1, 0));
        float front = output.HeightAt(x, Mathf.Min(y + 1, last));

        return new Vector3(left - right, 2.0f * step, back - front).Normalized();
    }

    private static Image LoadAlbedo(LandscapeMaterial? material, int slot, AssetSystem assets)
    {
        if (material is { TexturePath.Length: > 0 } &&
            assets.LoadTextureAsset(material.TexturePath) is { } texture)
        {
            Image image = texture.GetImage();
            image.Resize(PlaceholderSize, PlaceholderSize);
            image.Convert(Image.Format.Rgba8);
            return image;
        }

        return Placeholder(slot);
    }

    // A per-slot checker so an unassigned material is obviously unassigned rather than plausibly grey.
    private static Image Placeholder(int slot)
    {
        var image = Image.CreateEmpty(PlaceholderSize, PlaceholderSize, false, Image.Format.Rgba8);
        Color light = Color.FromHsv((slot * 0.17f) % 1.0f, 0.25f, 0.62f);
        Color dark = Color.FromHsv((slot * 0.17f) % 1.0f, 0.35f, 0.48f);

        for (int y = 0; y < PlaceholderSize; y++)
        {
            for (int x = 0; x < PlaceholderSize; x++)
            {
                bool even = ((x / 8) + (y / 8)) % 2 == 0;
                image.SetPixel(x, y, even ? light : dark);
            }
        }

        return image;
    }

    private static Image AlphaImage(byte[] alpha, int resolution)
    {
        var image = Image.CreateEmpty(resolution, resolution, false, Image.Format.R8);
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float value = alpha[(y * resolution) + x] / 255.0f;
                image.SetPixel(x, y, new Color(value, value, value));
            }
        }

        return image;
    }

    private static Shader SplatShader() => _shader ??= new Shader { Code = SplatShaderCode };

    // Kept in source rather than a .gdshader resource so it needs no Godot import step. Slots
    // composite back to front, each alpha slot over everything below it — the "over" rule the
    // resolver's merge maths assumes.
    private const string SplatShaderCode = """
shader_type spatial;
render_mode cull_back, diffuse_burley;

uniform sampler2DArray slot_albedo : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2DArray slot_alpha : filter_linear, repeat_disable;
uniform int slot_count = 1;
uniform float tiling = 8.0;
uniform bool show_chunk_edges = false;
uniform vec3 chunk_edge_color : source_color = vec3(0.95, 0.75, 0.35);

void fragment() {
    vec2 tiled = UV * tiling;
    vec3 color = texture(slot_albedo, vec3(tiled, 0.0)).rgb;

    for (int i = 1; i < slot_count; i++) {
        float coverage = texture(slot_alpha, vec3(UV, float(i - 1))).r;
        color = mix(color, texture(slot_albedo, vec3(tiled, float(i))).rgb, coverage);
    }

    // UV spans exactly one chunk, so its 0 and 1 edges are the chunk boundary. Drawing the border
    // here rather than on the ground grid means it follows the terrain over hills and can never
    // fight it for depth.
    if (show_chunk_edges) {
        vec2 toEdge = min(UV, vec2(1.0) - UV);
        vec2 aa = fwidth(UV) * 1.5;
        float edge = 1.0 - clamp(min(toEdge.x / aa.x, toEdge.y / aa.y), 0.0, 1.0);
        color = mix(color, chunk_edge_color, edge * 0.8);
    }

    ALBEDO = color;
    ROUGHNESS = 0.9;
    SPECULAR = 0.1;
}
""";
}
