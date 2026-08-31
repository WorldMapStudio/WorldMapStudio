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
        var colors = new Color[resolution * resolution];

        // Vertex light has no built-in Godot array slot, so it rides Custom0 as a plain RGB float
        // channel — flattened because that is the shape AddSurfaceFromArrays expects for a
        // Mesh.ArrayCustomFormat.RgbFloat channel (a PackedFloat32Array, 3 floats per vertex).
        var light = new float[resolution * resolution * 3];

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int index = (y * resolution) + x;
                vertices[index] = new Vector3(x * step, output.HeightAt(x, y), y * step);
                uvs[index] = new Vector2((float)x / quads, (float)y / quads);
                normals[index] = NormalAt(output, x, y, step);
                colors[index] = output.VertexColorAt(x, y);

                Color glow = output.VertexLightAt(x, y);
                light[index * 3] = glow.R;
                light[(index * 3) + 1] = glow.G;
                light[(index * 3) + 2] = glow.B;
            }
        }

        int[] indices = BuildIndices(resolution, output.Holes, output.HoleResolution);

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Custom0] = light;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        Mesh.ArrayFormat customFlags =
            (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: customFlags);
        return mesh;
    }

    /// <summary>
    /// The triangle indices of a chunk grid, wound so the surface faces up. Fully dense — no quad is
    /// skipped. Kept for callers with no hole grid to test against.
    ///
    /// <b>Godot's front face is the one whose vertices are clockwise as seen from the front</b>, the
    /// opposite of the OpenGL habit. Getting this backwards produces a mesh that builds, reports
    /// upward normals, and is invisible from above because every triangle is culled — so the winding
    /// is pulled out here where a test can pin it.
    ///
    /// Separated from <see cref="BuildMesh"/> so it can be checked without a live engine.
    /// </summary>
    public static int[] BuildIndices(int resolution) => BuildIndices(resolution, null, 0);

    /// <summary>
    /// As above, but skipping the 6 indices of any quad whose owning hole cell is set. A hole cell may
    /// be coarser than the vertex grid — several quads then share one cell, matching how export targets
    /// like WoW's ADT format keep holes at a fixed, low resolution independent of the vertex count.
    /// Vertices themselves are never dropped: the height data under a hole still exists, so a
    /// neighbouring closed quad keeps a clean normal across the cut edge.
    /// </summary>
    public static int[] BuildIndices(int resolution, bool[]? holes, int holeResolution)
    {
        int quads = resolution - 1;
        var indices = new int[quads * quads * 6];
        int next = 0;

        for (int y = 0; y < quads; y++)
        {
            for (int x = 0; x < quads; x++)
            {
                if (IsHoledQuad(holes, holeResolution, quads, x, y))
                {
                    continue;
                }

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

        System.Array.Resize(ref indices, next);
        return indices;
    }

    private static bool IsHoledQuad(bool[]? holes, int holeResolution, int quads, int x, int y)
    {
        if (holes == null || holeResolution <= 0 || quads <= 0)
        {
            return false;
        }

        int cellX = Mathf.Clamp((x * holeResolution) / quads, 0, holeResolution - 1);
        int cellY = Mathf.Clamp((y * holeResolution) / quads, 0, holeResolution - 1);
        return holes[(cellY * holeResolution) + cellX];
    }

    /// <summary>Whether chunk borders are drawn on the terrain. Set from the view settings.</summary>
    public static bool ShowChunkEdges { get; set; } = true;

    /// <summary>Builds the splatting material for a chunk's slots.</summary>
    public static ShaderMaterial BuildMaterial(LandscapeChunkOutput output, AssetSystem assets)
    {
        var material = new ShaderMaterial { Shader = SplatShader() };

        Texture2DArray albedoArray = AlbedoArrayCache.GetOrAdd(AlbedoArrayKey(output.Layers), _ => BuildAlbedoArray(output, assets));

        var alphas = new Godot.Collections.Array<Image>();
        for (int i = 0; i < output.Layers.Count; i++)
        {
            // Slot 0 is the opaque base and has no alpha; the array holds one image per alpha slot.
            if (output.Layers[i].Alpha is { } alpha)
            {
                alphas.Add(AlphaImage(alpha, output.AlphaResolution));
            }
        }

        // A sampler2DArray needs at least one layer even when nothing composites over the base.
        if (alphas.Count == 0)
        {
            alphas.Add(Image.CreateEmpty(1, 1, false, Image.Format.R8));
        }

        var alphaArray = new Texture2DArray();
        alphaArray.CreateFromImages(alphas);

        material.SetShaderParameter("slot_albedo", albedoArray);
        material.SetShaderParameter("slot_alpha", alphaArray);
        material.SetShaderParameter("slot_count", Mathf.Max(1, output.Layers.Count));
        material.SetShaderParameter("tiling", TextureTiling);
        material.SetShaderParameter("show_chunk_edges", ShowChunkEdges);
        return material;
    }

    // Chunks streamed from disjoint parts of the map routinely resolve to the exact same ordered set
    // of slot materials (a zone's terrain is typically a handful of recurring texture combinations),
    // and building a Texture2DArray means real Godot resource construction and a GPU upload — paid
    // again for every chunk even when its content is byte-identical to one already built. Keyed on
    // the layers' texture paths in order (order matters: index i is what the shader composites at
    // slot i) rather than their material identities, so an edit that changes a material's texture in
    // place naturally invalidates the right entries instead of returning a stale array — the same
    // invalidation-free reasoning AlbedoCache below already relies on.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Texture2DArray> AlbedoArrayCache = new();

    private static string AlbedoArrayKey(IReadOnlyList<LandscapeChunkLayer> layers)
    {
        var key = new System.Text.StringBuilder();
        foreach (LandscapeChunkLayer layer in layers)
        {
            key.Append(layer.Material?.TexturePath ?? "").Append('|');
        }

        return key.ToString();
    }

    private static Texture2DArray BuildAlbedoArray(LandscapeChunkOutput output, AssetSystem assets)
    {
        var albedos = new Godot.Collections.Array<Image>();
        for (int i = 0; i < output.Layers.Count; i++)
        {
            albedos.Add(LoadAlbedo(output.Layers[i].Material, i, assets));
        }

        // A chunk can resolve to no slots at all: nothing claimed a base and the map has no fallback
        // material, which is exactly the state a landscape starts in before anything is authored.
        // That is a reported problem, not a crash — render the placeholder so the chunk is visibly
        // unassigned rather than absent.
        if (albedos.Count == 0)
        {
            albedos.Add(Placeholder(0));
        }

        var array = new Texture2DArray();
        array.CreateFromImages(albedos);
        return array;
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

    // Chunks streamed from disjoint parts of the map routinely share a slot's material, and every
    // rebuild (streaming a new chunk in, or an edit marking one dirty) otherwise pays for decoding,
    // resizing and converting that material's source texture again from scratch. Keyed on the path
    // rather than the material instance so materials that happen to reference the same texture also
    // share one decode. Never invalidated: a texture path is immutable once authored, and reassigning
    // a slot's material entirely produces a new path to key on.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Image> AlbedoCache = new();

    private static Image LoadAlbedo(LandscapeMaterial? material, int slot, AssetSystem assets)
    {
        if (material is not { TexturePath.Length: > 0 } materialWithTexture)
        {
            return Placeholder(slot);
        }

        if (AlbedoCache.TryGetValue(materialWithTexture.TexturePath, out Image? cached))
        {
            return cached;
        }

        if (assets.LoadTextureAsset(materialWithTexture.TexturePath) is not { } texture)
        {
            return Placeholder(slot);
        }

        Image image = texture.GetImage();
        image.Resize(PlaceholderSize, PlaceholderSize);
        image.Convert(Image.Format.Rgba8);

        // Texture2DArray.CreateFromImages only reads pixels at upload time, so a shared, never-mutated
        // Image is safe to hand to every chunk that resolves to this slot.
        return AlbedoCache.GetOrAdd(materialWithTexture.TexturePath, image);
    }

    // A per-slot checker so an unassigned material is obviously unassigned rather than plausibly grey.
    // Built as a raw pixel buffer rather than per-pixel SetPixel calls: each SetPixel is a marshaled
    // native call, and there are PlaceholderSize^2 of them — thousands of icalls for what is otherwise
    // a few microseconds of managed array writes.
    private static Image Placeholder(int slot)
    {
        Color light = Color.FromHsv((slot * 0.17f) % 1.0f, 0.25f, 0.62f);
        Color dark = Color.FromHsv((slot * 0.17f) % 1.0f, 0.35f, 0.48f);
        byte[] lightBytes = ToRgba8(light);
        byte[] darkBytes = ToRgba8(dark);

        var pixels = new byte[PlaceholderSize * PlaceholderSize * 4];
        for (int y = 0; y < PlaceholderSize; y++)
        {
            for (int x = 0; x < PlaceholderSize; x++)
            {
                bool even = ((x / 8) + (y / 8)) % 2 == 0;
                (even ? lightBytes : darkBytes).CopyTo(pixels, ((y * PlaceholderSize) + x) * 4);
            }
        }

        return Image.CreateFromData(PlaceholderSize, PlaceholderSize, false, Image.Format.Rgba8, pixels);
    }

    private static byte[] ToRgba8(Color color) =>
    [
        (byte)Mathf.Clamp(Mathf.RoundToInt(color.R * 255.0f), 0, 255),
        (byte)Mathf.Clamp(Mathf.RoundToInt(color.G * 255.0f), 0, 255),
        (byte)Mathf.Clamp(Mathf.RoundToInt(color.B * 255.0f), 0, 255),
        255,
    ];

    // The alpha buffer is already row-major R8 coverage in [0, 255] — exactly what an R8 Image needs —
    // so this hands it straight to the image instead of unpacking each byte through a Color and back
    // via a per-pixel SetPixel call.
    private static Image AlphaImage(byte[] alpha, int resolution) =>
        Image.CreateFromData(resolution, resolution, false, Image.Format.R8, alpha);

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

// CUSTOM0 (vertex light) is a vertex-stage built-in only, so it needs a varying to reach fragment();
// COLOR (vertex color) is exposed in both stages and needs none.
varying vec3 vertex_light;

void vertex() {
    vertex_light = CUSTOM0.rgb;
}

void fragment() {
    vec2 tiled = UV * tiling;
    vec3 color = texture(slot_albedo, vec3(tiled, 0.0)).rgb;

    for (int i = 1; i < slot_count; i++) {
        float coverage = texture(slot_alpha, vec3(UV, float(i - 1))).r;
        color = mix(color, texture(slot_albedo, vec3(tiled, float(i))).rgb, coverage);
    }

    // Traditional vertex color shades the splatted albedo multiplicatively; vertex light is a
    // separate additive layer on top, matching how the two are evaluated on the CPU (white/black
    // defaults so a chunk binding neither renders exactly as it did before either existed).
    color *= COLOR.rgb;
    color += vertex_light;

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
