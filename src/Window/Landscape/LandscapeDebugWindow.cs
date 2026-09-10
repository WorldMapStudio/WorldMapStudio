using System.Collections.Generic;
using System.Linq;
using Godot;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// Shows what the builder actually produced for the chunk under the camera: its heightmap, the alpha
/// of each resolved slot, and the problems the last build reported.
///
/// This is the primary debugging surface for everything from here on. A chunk that looks wrong is
/// much easier to explain when you can see whether its channel was empty, its alpha was flat, or its
/// claim was dropped.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class LandscapeDebugWindow : Window
{
    public override string? Category => "Landscape";
    public override KeyboardShortcut DefaultShortcut => new(ImGuiKey.B, ShortcutModifiers.Alt);

    private const int PreviewSize = 128;

    private readonly EditorContext _context;
    private readonly List<ImageTexture> _textures = [];
    private ImageTexture? _holeTexture;

    private ChunkCoord? _inspected;
    private int _shownVersion = -1;

    public LandscapeDebugWindow(WindowManager manager)
        : base("Landscape Debug", startOpen: false, defaultSize: new NVector2(420.0f, 620.0f))
    {
        _context = manager.Context;
    }

    protected override void DrawContent()
    {
        if (!_context.Landscape.IsEnabled)
        {
            ImGui.TextDisabled("This map has no landscape.");
            return;
        }

        if (NearestChunk() is not (ChunkCoord coord, LandscapeChunkOutput output))
        {
            ImGui.TextDisabled("No chunk loaded near the camera.");
            return;
        }

        if (_inspected != coord || _shownVersion != _context.Scene.Version)
        {
            _inspected = coord;
            _shownVersion = _context.Scene.Version;
            Refresh(output);
        }

        ImGui.Text($"Chunk {output.Coord}");
        ImGui.TextDisabled($"{output.Layers.Count} slots · {output.HeightResolution}² vertices");

        (float min, float max) = HeightRange(output);
        ImGui.TextDisabled($"height {min:0.##} … {max:0.##}");

        LandscapeRebuilder rebuilder = _context.Landscape.Rebuilder;
        if (rebuilder.IsBuilding || rebuilder.PendingChunks > 0)
        {
            ImGui.TextColored(new NVector4(1.0f, 0.72f, 0.22f, 1.0f),
                $"rebuilding — {rebuilder.PendingChunks} chunks queued");
        }

        if (rebuilder.LastWaveChunks > 0)
        {
            ImGui.TextDisabled($"last wave — {rebuilder.LastWaveChunks} chunks in {rebuilder.LastWaveMs:0.#} ms");
        }

        ImGui.Separator();
        DrawPreviews(output);
        ImGui.Separator();
        DrawAttributes(output);
        ImGui.Separator();
        DrawProblems();
    }

    // The value of each declared attribute in this chunk, name-resolved. An attribute nothing wrote
    // is absent from output.Attributes — shown as its declared default so it is not mistaken for
    // "unwritten means zero".
    private void DrawAttributes(LandscapeChunkOutput output)
    {
        IReadOnlyList<TerrainAttribute> declared = _context.Landscape.Catalog.Attributes;
        if (declared.Count == 0)
        {
            ImGui.TextDisabled("No terrain attributes declared.");
            return;
        }

        List<TerrainAttributeValue> allValues = _context.Catalog.OfType<TerrainAttributeValue>().ToList();

        foreach (TerrainAttribute attribute in declared)
        {
            bool written = output.Attributes.TryGetValue(attribute.Key, out TerrainAttributeGrid grid);
            string cells = attribute.CellsPerChunkEdge > 1 ? $" · {attribute.CellsPerChunkEdge}² cells (showing 0,0)" : "";
            ImGui.Text($"{attribute.Name} [{attribute.Key}]{(written ? cells : " · default")}");

            int components = written ? grid.Components : attribute.Components;
            for (int c = 0; c < components; c++)
            {
                long raw = written ? grid.At(0, 0, c) : attribute.DefaultValue;
                string label = components > 1 ? $"  {attribute.ComponentLabel(c)}: " : "  ";
                ImGui.TextDisabled($"{label}{raw}  ({TerrainAttributeNames.Resolve(attribute, raw, allValues)})");
            }
        }
    }

    private void DrawPreviews(LandscapeChunkOutput output)
    {
        if (_textures.Count == 0)
        {
            ImGui.TextDisabled("Nothing to preview.");
            return;
        }

        ImGui.Text("Height");
        Preview(_textures[0]);

        for (int i = 1; i < _textures.Count; i++)
        {
            LandscapeChunkLayer layer = output.Layers[i];
            ImGui.Text($"Slot {i} alpha — {layer.Material?.Name ?? "(none)"}");
            Preview(_textures[i]);
        }

        if (_holeTexture != null)
        {
            ImGui.Text("Holes");
            Preview(_holeTexture);
        }
    }

    // ImGui takes the Godot RID as its texture handle, the same way map thumbnails do.
    private static void Preview(ImageTexture texture) =>
        ImGui.Image((System.IntPtr)texture.GetRid().Id, new NVector2(PreviewSize, PreviewSize));

    private void DrawProblems()
    {
        int errors = _context.Problems.CountOf(ProblemSeverity.Error);
        int warnings = _context.Problems.CountOf(ProblemSeverity.Warning);

        if (errors == 0 && warnings == 0)
        {
            ImGui.TextColored(new NVector4(0.42f, 0.85f, 0.46f, 1.0f), "No problems.");
            return;
        }

        ImGui.TextColored(
            errors > 0 ? new NVector4(1.0f, 0.45f, 0.4f, 1.0f) : new NVector4(1.0f, 0.72f, 0.22f, 1.0f),
            $"{errors} errors, {warnings} warnings — see the Problems window.");
    }

    // Rebuilds the preview textures. Called only when the chunk or the scene changed, since this
    // allocates images.
    private void Refresh(LandscapeChunkOutput output)
    {
        _textures.Clear();

        (float min, float max) = HeightRange(output);
        float span = Mathf.Max(max - min, 0.0001f);
        _textures.Add(Grayscale(output.HeightResolution, (x, y) => (output.HeightAt(x, y) - min) / span));

        // Slot 0 is the opaque base and has no alpha, so previews start at slot 1.
        for (int i = 1; i < output.Layers.Count; i++)
        {
            byte[]? alpha = output.Layers[i].Alpha;
            int resolution = output.AlphaResolution;
            _textures.Add(alpha == null
                ? Grayscale(1, (_, _) => 0.0f)
                : Grayscale(resolution, (x, y) => alpha[(y * resolution) + x] / 255.0f));
        }

        _holeTexture = Grayscale(output.HoleResolution, (x, y) => output.IsHole(x, y) ? 1.0f : 0.0f);
    }

    private static ImageTexture Grayscale(int resolution, System.Func<int, int, float> value)
    {
        var image = Image.CreateEmpty(resolution, resolution, false, Image.Format.Rgba8);
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float level = Mathf.Clamp(value(x, y), 0.0f, 1.0f);
                image.SetPixel(x, y, new Color(level, level, level));
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static (float Min, float Max) HeightRange(LandscapeChunkOutput output)
    {
        float min = float.MaxValue;
        float max = float.MinValue;
        foreach (float height in output.Heights)
        {
            min = Mathf.Min(min, height);
            max = Mathf.Max(max, height);
        }

        return output.Heights.Length == 0 ? (0.0f, 0.0f) : (min, max);
    }

    private (ChunkCoord Coord, LandscapeChunkOutput Output)? NearestChunk()
    {
        if (_context.Landscape.Grid is not { } grid)
        {
            return null;
        }

        Vector3 focus = _context.Landscape.Focus;
        (ChunkCoord Coord, LandscapeChunkOutput Output)? best = null;
        float bestDistance = float.MaxValue;

        foreach (KeyValuePair<ChunkCoord, (LandscapeTerrainBatch Batch, LandscapeChunkOutput Output)> entry in _context.Landscape.ChunkIndex.ByCoord)
        {
            float distance = grid.OriginOf(entry.Key).DistanceSquaredTo(focus);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = (entry.Key, entry.Value.Output);
            }
        }

        return best;
    }
}
