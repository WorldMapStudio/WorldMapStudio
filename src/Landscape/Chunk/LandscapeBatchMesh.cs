using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Turns a square of chunks into one thing the viewport can draw: a single CPU-built mesh, one
/// splatting material, and the texture arrays behind it. The chunks keep their own vertex grids and
/// resolved slots — this only concatenates them and adds the per-chunk indirection the shared shader
/// needs to composite each chunk's own slot list.
///
/// The editor's renderer is deliberately not bound by the map's texture limit: how many samplers the
/// preview uses is its own business, so slots go into uniform-count arrays with no per-limit variant.
/// </summary>
public static class LandscapeBatchMesh
{
    /// <summary>Placeholder texture size used until materials load real images.</summary>
    private const int PlaceholderSize = 64;

    /// <summary>How many times a slot texture tiles across one chunk.</summary>
    private const float TextureTiling = 8.0f;

    /// <summary>The shader's compile-time per-chunk slot loop bound and the slot-map width; matches
    /// <see cref="LandscapeTerrainBatch.MaxSlots"/>. This bounds one <em>chunk</em>'s resolved slots,
    /// not the batch — a batch pools every distinct material its chunks use.</summary>
    private const int MaxSlots = LandscapeTerrainBatch.MaxSlots;

    /// <summary>How many distinct materials one batch can pool. The slot-map stores an array layer
    /// index in an 8-bit channel, and layer 0 is the "no material" placeholder, so 255 real ones fit
    /// — far more than a texture array's layer limit would ever let render anyway.</summary>
    private const int MaxBatchLayers = 255;

    private static Shader? _shader;

    /// <summary>Whether chunk borders are drawn on the terrain. Set from the view settings.</summary>
    public static bool ShowChunkEdges { get; set; } = true;

    /// <summary>Whether baked vertex color tints the terrain. Set from the view settings.</summary>
    public static bool ShowVertexColor { get; set; } = true;

    /// <summary>Whether baked vertex light lights the terrain. Set from the view settings.</summary>
    public static bool ShowVertexLight { get; set; } = true;

    /// <summary>
    /// One surface for the whole batch, built by concatenating each chunk's own vertex grid. Chunk
    /// positions are batch-local; UV stays chunk-local (the shader tiles and draws chunk edges from
    /// it); Custom1 carries the chunk's index inside the batch so the fragment shader can look up that
    /// chunk's slots.
    /// </summary>
    public static ArrayMesh BuildMesh(
        IReadOnlyList<(ChunkCoord Coord, LandscapeChunkOutput Output)> chunks,
        LandscapeBatchCoord batchCoord,
        LandscapeGrid grid,
        int batchChunks)
    {
        ChunkCoord batchOrigin = batchCoord.Origin(batchChunks);

        int totalVerts = 0;
        int maxIndices = 0;
        foreach ((ChunkCoord _, LandscapeChunkOutput output) in chunks)
        {
            int r = output.HeightResolution;
            totalVerts += r * r;
            maxIndices += (r - 1) * (r - 1) * 6;
        }

        var vertices = new Vector3[totalVerts];
        var normals = new Vector3[totalVerts];
        var uvs = new Vector2[totalVerts];
        var vertexColor = new float[totalVerts * 4];
        var light = new float[totalVerts * 3];
        var chunkIndex = new float[totalVerts];
        var indices = new int[maxIndices];

        int v = 0;
        int idx = 0;
        foreach ((ChunkCoord coord, LandscapeChunkOutput output) in chunks)
        {
            int resolution = output.HeightResolution;
            int quads = resolution - 1;
            float step = grid.ChunkSize / quads;
            float offsetX = (coord.X - batchOrigin.X) * grid.ChunkSize;
            float offsetZ = (coord.Y - batchOrigin.Y) * grid.ChunkSize;
            float index = batchCoord.IndexOf(coord, batchChunks);
            int vertexBase = v;

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    vertices[v] = new Vector3(offsetX + (x * step), output.HeightAt(x, y), offsetZ + (y * step));
                    uvs[v] = new Vector2((float)x / quads, (float)y / quads);
                    normals[v] = NormalAt(output, x, y, step);

                    Color tint = output.VertexColorAt(x, y);
                    vertexColor[v * 4] = tint.R;
                    vertexColor[(v * 4) + 1] = tint.G;
                    vertexColor[(v * 4) + 2] = tint.B;
                    vertexColor[(v * 4) + 3] = 1.0f;

                    Color glow = output.VertexLightAt(x, y);
                    light[v * 3] = glow.R;
                    light[(v * 3) + 1] = glow.G;
                    light[(v * 3) + 2] = glow.B;

                    chunkIndex[v] = index;
                    v++;
                }
            }

            foreach (int i in BuildIndices(resolution, output.Holes, output.HoleResolution))
            {
                indices[idx++] = vertexBase + i;
            }
        }

        if (idx != indices.Length)
        {
            System.Array.Resize(ref indices, idx);
        }

        // Disposed as soon as AddSurfaceFromArrays has copied it.
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Custom0] = light;
        arrays[(int)Mesh.ArrayType.Custom1] = chunkIndex;
        arrays[(int)Mesh.ArrayType.Custom2] = vertexColor;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        Mesh.ArrayFormat customFlags =
            (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift) |
            (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift) |
            (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom2Shift);

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
    /// </summary>
    public static int[] BuildIndices(int resolution) => BuildIndices(resolution, null, 0);

    /// <summary>
    /// As above, but skipping the 6 indices of any quad whose owning hole cell is set. A hole cell may
    /// be coarser than the vertex grid. Vertices themselves are never dropped: the height data under a
    /// hole still exists, so a neighbouring closed quad keeps a clean normal across the cut edge.
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

    /// <summary>
    /// Builds the one splatting material for a batch. A chunk resolves to its own ordered slot list;
    /// the slot-map texture tells the shader, per chunk and per slot, which array layer that slot's
    /// material uses so a single material can serve every chunk in the batch.
    /// </summary>
    public static ShaderMaterial BuildMaterial(
        IReadOnlyList<(ChunkCoord Coord, LandscapeChunkOutput Output)> chunks,
        LandscapeBatchCoord batchCoord,
        int batchChunks,
        AssetSystem assets,
        LandscapeSettings settings)
    {
        var material = new ShaderMaterial { Shader = SplatShader() };

        List<LandscapeMaterial> slotMaterials = BatchSlotMaterials(chunks, batchCoord, batchChunks);
        Dictionary<object, int> layerOf = LayerIndices(slotMaterials);
        int alphaResolution = Mathf.Max(1, settings.ChunkAlphaResolution);

        Texture2DArray albedoArray = AlbedoArrayCache.GetOrAdd(
            MaterialSetKey(slotMaterials), _ => BuildAlbedoArray(slotMaterials, assets));

        Texture2DArray alphaArray = BuildAlphaAtlas(AlphaAtlasLayers(chunks, batchCoord, batchChunks, alphaResolution), alphaResolution, batchChunks);
        ImageTexture slotMap = BuildSlotMap(SlotMapBytes(chunks, batchCoord, batchChunks, layerOf), batchChunks);

        material.SetShaderParameter(Names.SlotAlbedo, albedoArray);
        material.SetShaderParameter(Names.SlotAlpha, alphaArray);
        material.SetShaderParameter(Names.SlotMap, slotMap);
        material.SetShaderParameter(Names.BatchChunks, batchChunks);
        material.SetShaderParameter(Names.AlphaResolution, alphaResolution);
        material.SetShaderParameter(Names.Tiling, TextureTiling);
        ApplyDisplayToggles(material);
        material.SetShaderParameter(Names.BlendMode, (int)settings.TextureBlendMode);

        if (settings.TextureBlendMode == LandscapeTextureBlendMode.HeightBased)
        {
            Texture2DArray heightArray = HeightArrayCache.GetOrAdd(
                MaterialSetKey(slotMaterials), _ => BuildHeightArray(slotMaterials, assets));
            material.SetShaderParameter(Names.SlotHeight, heightArray);
        }

        return material;
    }

    // The distinct materials any slot of any chunk in the batch resolves to, ordered deterministically
    // so the texture arrays can be cached and shared between batches. Keyed by RecordId when there is
    // one, else by reference. A batch pools far more than one chunk's worth — the cap here is only the
    // slot-map channel's 255, which no real map approaches.
    private static List<LandscapeMaterial> BatchSlotMaterials(
        IReadOnlyList<(ChunkCoord Coord, LandscapeChunkOutput Output)> chunks,
        LandscapeBatchCoord batchCoord,
        int batchChunks)
    {
        var seen = new Dictionary<object, LandscapeMaterial>();
        foreach ((ChunkCoord _, LandscapeChunkOutput output) in chunks)
        {
            foreach (LandscapeChunkLayer layer in output.Layers)
            {
                if (layer.Material is { } m)
                {
                    seen.TryAdd(KeyOf(m), m);
                }
            }
        }

        List<LandscapeMaterial> ordered = seen.Values
            .OrderBy(m => m.RecordId ?? int.MaxValue)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        if (ordered.Count > MaxBatchLayers)
        {
            GD.PushWarning(
                $"[Landscape] Terrain batch {batchCoord.X},{batchCoord.Y} pools {ordered.Count} distinct " +
                $"materials; keeping the first {MaxBatchLayers}.");
            ordered = ordered.Take(MaxBatchLayers).ToList();
        }

        return ordered;
    }

    private static object KeyOf(LandscapeMaterial material) =>
        material.RecordId is int id ? id : material;

    // Layer 0 is the "no material" placeholder, so real materials start at layer 1.
    private static Dictionary<object, int> LayerIndices(IReadOnlyList<LandscapeMaterial> ordered)
    {
        var layerOf = new Dictionary<object, int>();
        for (int i = 0; i < ordered.Count; i++)
        {
            layerOf[KeyOf(ordered[i])] = i + 1;
        }

        return layerOf;
    }

    // Identity of the ordered material set, so two batches that resolve to the same materials share
    // one texture array. Not the texture paths: two materials can share a texture but differ in the
    // height half, and the identity key already tells those apart.
    private static string MaterialSetKey(IReadOnlyList<LandscapeMaterial> ordered) =>
        string.Join('|', ordered.Select(m => m.RecordId?.ToString() ?? m.Name));

    // Chunks streamed from disjoint parts of the map routinely resolve to the same ordered material
    // set, and building a Texture2DArray is real Godot resource construction plus a GPU upload.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Texture2DArray> AlbedoArrayCache = new();

    private static Texture2DArray BuildAlbedoArray(IReadOnlyList<LandscapeMaterial> ordered, AssetSystem assets) =>
        BuildLayerArray(ordered.Count + 1, i => i == 0 ? (Placeholder(0), false) : LoadAlbedo(ordered[i - 1], i, assets));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Texture2DArray> HeightArrayCache = new();

    private static Texture2DArray BuildHeightArray(IReadOnlyList<LandscapeMaterial> ordered, AssetSystem assets) =>
        BuildLayerArray(ordered.Count + 1, i => i == 0 ? (NeutralHeight(), false) : LoadHeight(ordered[i - 1], assets));

    // CreateFromImages copies the pixels straight away, so every source image that is not one of the
    // shared decode-cache entries is disposed here rather than left for the finalizer thread — a wave
    // of undisposed Images is exactly what the finalizer was drowning in.
    private static Texture2DArray BuildLayerArray(int count, System.Func<int, (Image Image, bool Shared)> layer)
    {
        var images = new Godot.Collections.Array<Image>();
        var throwaway = new List<Image>();
        for (int i = 0; i < count; i++)
        {
            (Image image, bool shared) = layer(i);
            images.Add(image);
            if (!shared)
            {
                throwaway.Add(image);
            }
        }

        var array = new Texture2DArray();
        array.CreateFromImages(images);

        foreach (Image image in throwaway)
        {
            image.Dispose();
        }

        return array;
    }

    /// <summary>The array layer each material of the batch's ordered slot set uses, keyed by material
    /// identity. Layer 0 is the "no material" placeholder, so real materials start at 1. Pure — no
    /// Godot — so a test can pin the packing that depends on it.</summary>
    internal static Dictionary<object, int> BatchLayerIndices(
        IReadOnlyList<(ChunkCoord Coord, LandscapeChunkOutput Output)> chunks,
        LandscapeBatchCoord batchCoord,
        int batchChunks) =>
        LayerIndices(BatchSlotMaterials(chunks, batchCoord, batchChunks));

    /// <summary>
    /// One R8 buffer per alpha atlas layer. Layers are indexed by a chunk's own slot ordering (slot 0
    /// is the opaque base and carries no alpha, so layer k-1 holds chunk-local slot k); chunks are
    /// kept apart by tile, so two chunks whose slot 1 differs never collide even in one layer. Pure.
    /// </summary>
    internal static byte[][] AlphaAtlasLayers(
        IReadOnlyList<(ChunkCoord Coord, LandscapeChunkOutput Output)> chunks,
        LandscapeBatchCoord batchCoord,
        int batchChunks,
        int alphaResolution)
    {
        int maxLocalSlots = chunks.Count == 0 ? 1 : chunks.Max(entry => entry.Output.Layers.Count);
        int layers = Mathf.Max(1, maxLocalSlots - 1);
        int tileStride = batchChunks * alphaResolution;

        var layerBytes = new byte[layers][];
        for (int i = 0; i < layers; i++)
        {
            layerBytes[i] = new byte[tileStride * tileStride];
        }

        foreach ((ChunkCoord coord, LandscapeChunkOutput output) in chunks)
        {
            int c = batchCoord.IndexOf(coord, batchChunks);
            if (c < 0)
            {
                continue;
            }

            int px = (c % batchChunks) * alphaResolution;
            int py = (c / batchChunks) * alphaResolution;

            for (int k = 1; k < output.Layers.Count; k++)
            {
                if (output.Layers[k].Alpha is not { } alpha || k - 1 >= layers)
                {
                    continue;
                }

                byte[] dest = layerBytes[k - 1];
                for (int y = 0; y < alphaResolution; y++)
                {
                    int destRow = ((py + y) * tileStride) + px;
                    int srcRow = y * alphaResolution;
                    for (int x = 0; x < alphaResolution; x++)
                    {
                        dest[destRow + x] = alpha[srcRow + x];
                    }
                }
            }
        }

        return layerBytes;
    }

    private static Texture2DArray BuildAlphaAtlas(byte[][] layerBytes, int alphaResolution, int batchChunks)
    {
        int tileStride = batchChunks * alphaResolution;
        var images = new List<Image>();
        foreach (byte[] bytes in layerBytes)
        {
            images.Add(Image.CreateFromData(tileStride, tileStride, false, Image.Format.R8, bytes));
        }

        var godotImages = new Godot.Collections.Array<Image>(images);
        var array = new Texture2DArray();
        array.CreateFromImages(godotImages);

        // CreateFromImages has uploaded the pixels; release now rather than leave a wave of these to
        // the finalizer, which contends with the threads still building batches.
        foreach (Image image in images)
        {
            image.Dispose();
        }

        return array;
    }

    /// <summary>
    /// The raw Rgba8 buffer of the slot-map texture: width MaxSlots, height batchChunks². Pixel
    /// (s, c) describes chunk c's slot s — r = the array layer that slot's material uses, a = 255
    /// while the slot exists and 0 past the end of that chunk's slot list. Pure.
    /// </summary>
    internal static byte[] SlotMapBytes(
        IReadOnlyList<(ChunkCoord Coord, LandscapeChunkOutput Output)> chunks,
        LandscapeBatchCoord batchCoord,
        int batchChunks,
        IReadOnlyDictionary<object, int> layerOf)
    {
        int cells = batchChunks * batchChunks;
        var pixels = new byte[MaxSlots * cells * 4];

        foreach ((ChunkCoord coord, LandscapeChunkOutput output) in chunks)
        {
            int c = batchCoord.IndexOf(coord, batchChunks);
            if (c < 0)
            {
                continue;
            }

            for (int s = 0; s < output.Layers.Count && s < MaxSlots; s++)
            {
                int layer = output.Layers[s].Material is { } m && layerOf.TryGetValue(KeyOf(m), out int found) ? found : 0;
                int at = (((c * MaxSlots) + s) * 4);
                pixels[at] = (byte)Mathf.Clamp(layer, 0, 255);
                pixels[at + 3] = 255;
            }
        }

        return pixels;
    }

    private static ImageTexture BuildSlotMap(byte[] pixels, int batchChunks)
    {
        var image = Image.CreateFromData(MaxSlots, batchChunks * batchChunks, false, Image.Format.Rgba8, pixels);
        ImageTexture texture = ImageTexture.CreateFromImage(image);
        image.Dispose();
        return texture;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Image> HeightCache = new();

    private static (Image Image, bool Shared) LoadHeight(LandscapeMaterial? material, AssetSystem assets)
    {
        if (material is not { BlendHeightTexturePath.Length: > 0 } materialWithHeight)
        {
            return (NeutralHeight(), false);
        }

        if (HeightCache.TryGetValue(materialWithHeight.BlendHeightTexturePath, out Image? cached))
        {
            return (cached, true);
        }

        if (assets.LoadTextureAsset(materialWithHeight.BlendHeightTexturePath) is not { } texture)
        {
            return (NeutralHeight(), false);
        }

        Image image = texture.GetImage();
        image.Resize(PlaceholderSize, PlaceholderSize);
        image.Convert(Image.Format.R8);
        Image stored = HeightCache.GetOrAdd(materialWithHeight.BlendHeightTexturePath, image);
        if (!ReferenceEquals(stored, image))
        {
            image.Dispose();
        }

        return (stored, true);
    }

    private static Image NeutralHeight()
    {
        var pixels = new byte[PlaceholderSize * PlaceholderSize];
        System.Array.Fill<byte>(pixels, 128);
        return Image.CreateFromData(PlaceholderSize, PlaceholderSize, false, Image.Format.R8, pixels);
    }

    private static Vector3 NormalAt(LandscapeChunkOutput output, int x, int y, float step)
    {
        int last = output.HeightResolution - 1;
        float left = output.HeightAt(Mathf.Max(x - 1, 0), y);
        float right = output.HeightAt(Mathf.Min(x + 1, last), y);
        float back = output.HeightAt(x, Mathf.Max(y - 1, 0));
        float front = output.HeightAt(x, Mathf.Min(y + 1, last));

        return new Vector3(left - right, 2.0f * step, back - front).Normalized();
    }

    // Keyed on the path so materials that reference the same texture share one decode. Never
    // invalidated: a texture path is immutable once authored.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Image> AlbedoCache = new();

    private static (Image Image, bool Shared) LoadAlbedo(LandscapeMaterial? material, int slot, AssetSystem assets)
    {
        if (material is not { TexturePath.Length: > 0 } materialWithTexture)
        {
            return (Placeholder(slot), false);
        }

        if (AlbedoCache.TryGetValue(materialWithTexture.TexturePath, out Image? cached))
        {
            return (cached, true);
        }

        if (assets.LoadTextureAsset(materialWithTexture.TexturePath) is not { } texture)
        {
            return (Placeholder(slot), false);
        }

        Image image = texture.GetImage();
        image.Resize(PlaceholderSize, PlaceholderSize);
        image.Convert(Image.Format.Rgba8);
        Image stored = AlbedoCache.GetOrAdd(materialWithTexture.TexturePath, image);
        if (!ReferenceEquals(stored, image))
        {
            image.Dispose();
        }

        return (stored, true);
    }

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

    /// <summary>Writes the current view toggles onto a splat material.</summary>
    public static void ApplyDisplayToggles(ShaderMaterial material)
    {
        material.SetShaderParameter(Names.ShowChunkEdges, ShowChunkEdges);
        material.SetShaderParameter(Names.ShowVertexColor, ShowVertexColor);
        material.SetShaderParameter(Names.ShowVertexLight, ShowVertexLight);
    }

    private static Shader SplatShader() => _shader ??= new Shader { Code = SplatShaderCode };

    // Cached rather than converted per SetShaderParameter call: each conversion allocates a
    // finalizable StringName.
    private static class Names
    {
        public static readonly StringName SlotAlbedo = "slot_albedo";
        public static readonly StringName SlotAlpha = "slot_alpha";
        public static readonly StringName SlotHeight = "slot_height";
        public static readonly StringName SlotMap = "slot_map";
        public static readonly StringName BatchChunks = "batch_chunks";
        public static readonly StringName AlphaResolution = "alpha_resolution";
        public static readonly StringName BlendMode = "blend_mode";
        public static readonly StringName Tiling = "tiling";
        public static readonly StringName ShowChunkEdges = "show_chunk_edges";
        public static readonly StringName ShowVertexColor = "show_vertex_color";
        public static readonly StringName ShowVertexLight = "show_vertex_light";
    }

    internal const string SplatShaderCode = """
shader_type spatial;
render_mode cull_back, diffuse_burley;

const int MAX_SLOTS = 16;

uniform sampler2DArray slot_albedo : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2DArray slot_alpha : filter_linear, repeat_disable;
uniform sampler2DArray slot_height : filter_linear, repeat_enable;
uniform sampler2D slot_map : filter_nearest, repeat_disable;
uniform int batch_chunks = 1;
uniform int alpha_resolution = 64;
uniform int blend_mode = 0; // LandscapeTextureBlendMode: 0 SequentialOver, 1 WeightedSum, 2 HeightBased
uniform float tiling = 8.0;
uniform bool show_chunk_edges = false;
uniform bool show_vertex_color = true;
uniform bool show_vertex_light = true;
uniform vec3 chunk_edge_color : source_color = vec3(0.95, 0.75, 0.35);

const float height_blend_sharpness = 8.0;

global uniform float wms_terrain_specular_intensity = 1.0;

""" + EnvironmentShaderLibrary.FogFunctionCode + """

// CUSTOM0 (vertex light), CUSTOM1 (this chunk's index inside the batch) and CUSTOM2 (vertex color, a
// 0-4 multiplier that must survive above 1.0 to brighten — see show_vertex_color below) are
// vertex-stage built-ins only, so each needs a varying to reach fragment(). chunk_index is flat: it is
// constant per chunk and must not be interpolated across the seam between two chunks in one surface.
varying vec3 vertex_light;
varying vec3 vertex_color;
varying vec3 world_pos;
varying flat float chunk_index;

void vertex() {
    vertex_light = CUSTOM0.rgb;
    chunk_index = CUSTOM1.x;
    vertex_color = CUSTOM2.rgb;
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

// This chunk's tile inside the batch-wide alpha atlas. UV is chunk-local, so it is inset by half a
// texel to reproduce the clamp-to-edge a per-chunk texture had before the tiles shared one image.
vec2 atlas_uv(vec2 uv) {
    float half_texel = 0.5 / float(alpha_resolution);
    vec2 tile = clamp(uv, vec2(half_texel), vec2(1.0 - half_texel));
    int c = int(chunk_index);
    vec2 cell = vec2(float(c % batch_chunks), float(c / batch_chunks));
    return (cell + tile) / float(batch_chunks);
}

// r = the array layer this chunk's slot i uses; a = 0 past its last slot.
vec4 slot_entry(int i) {
    return texelFetch(slot_map, ivec2(i, int(chunk_index)), 0);
}

float slot_layer(vec4 entry) {
    return float(int(entry.r * 255.0 + 0.5));
}

float slot_coverage(int i, vec2 uv) {
    return i == 0 ? 1.0 : texture(slot_alpha, vec3(atlas_uv(uv), float(i - 1))).r;
}

vec3 composite_sequential_over(vec2 tiled, vec2 uv) {
    vec3 color = texture(slot_albedo, vec3(tiled, slot_layer(slot_entry(0)))).rgb;
    for (int i = 1; i < MAX_SLOTS; i++) {
        vec4 e = slot_entry(i);
        if (e.a < 0.5) { break; }
        color = mix(color, texture(slot_albedo, vec3(tiled, slot_layer(e))).rgb, slot_coverage(i, uv));
    }
    return color;
}

vec3 composite_weighted_sum(vec2 tiled, vec2 uv) {
    float alpha_sum = 0.0;
    for (int i = 1; i < MAX_SLOTS; i++) {
        if (slot_entry(i).a < 0.5) { break; }
        alpha_sum += slot_coverage(i, uv);
    }

    vec3 color = texture(slot_albedo, vec3(tiled, slot_layer(slot_entry(0)))).rgb * clamp(1.0 - alpha_sum, 0.0, 1.0);
    for (int i = 1; i < MAX_SLOTS; i++) {
        vec4 e = slot_entry(i);
        if (e.a < 0.5) { break; }
        color += texture(slot_albedo, vec3(tiled, slot_layer(e))).rgb * slot_coverage(i, uv);
    }
    return color;
}

vec3 composite_height_based(vec2 tiled, vec2 uv) {
    float weight_sum = 0.0;
    for (int i = 0; i < MAX_SLOTS; i++) {
        vec4 e = slot_entry(i);
        if (e.a < 0.5) { break; }
        float h = texture(slot_height, vec3(tiled, slot_layer(e))).r;
        weight_sum += slot_coverage(i, uv) * exp(h * height_blend_sharpness);
    }
    weight_sum = max(weight_sum, 0.0001);

    vec3 color = vec3(0.0);
    for (int i = 0; i < MAX_SLOTS; i++) {
        vec4 e = slot_entry(i);
        if (e.a < 0.5) { break; }
        float h = texture(slot_height, vec3(tiled, slot_layer(e))).r;
        float w = (slot_coverage(i, uv) * exp(h * height_blend_sharpness)) / weight_sum;
        color += texture(slot_albedo, vec3(tiled, slot_layer(e))).rgb * w;
    }
    return color;
}

void fragment() {
    vec2 tiled = UV * tiling;
    vec3 color;
    if (blend_mode == 2) {
        color = composite_height_based(tiled, UV);
    } else if (blend_mode == 1) {
        color = composite_weighted_sum(tiled, UV);
    } else {
        color = composite_sequential_over(tiled, UV);
    }

    if (show_vertex_color) {
        color *= vertex_color;
    }

    // Baked vertex light is light, not pigment: it must add to what reaches the surface rather than
    // to the surface's own colour, so it survives a dark scene and isn't scaled up by a bright one.
    // EMISSION is exactly that — added after lighting — and is still tinted by the albedo under it.
    vec3 emission = show_vertex_light ? color * vertex_light : vec3(0.0);

    if (show_chunk_edges) {
        vec2 toEdge = min(UV, vec2(1.0) - UV);
        vec2 aa = fwidth(UV) * 1.5;
        float edge = 1.0 - clamp(min(toEdge.x / aa.x, toEdge.y / aa.y), 0.0, 1.0);
        color = mix(color, chunk_edge_color, edge * 0.8);
        emission *= 1.0 - edge;
    }

    ALBEDO = color;
    EMISSION = emission;
    ROUGHNESS = 0.9;

    float spec_alpha = texture(slot_albedo, vec3(tiled, slot_layer(slot_entry(0)))).a;
    SPECULAR = clamp(spec_alpha * wms_terrain_specular_intensity * 0.5, 0.0, 1.0);

    vec3 view_vec = world_pos - CAMERA_POSITION_WORLD;
    vec3 fog_color;
    float fog_visibility = wms_evaluate_fog(view_vec, world_pos.y, normalize(view_vec), 0, fog_color);
    FOG = vec4(fog_color, 1.0 - fog_visibility);
}
""";
}
