using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Read-only inspector for landscape chunks. Everything a chunk shows is computed, so there is
/// nothing to edit here — what it offers instead is why the chunk looks the way it does: which
/// materials took which slots, and which of them were merged.
/// </summary>
[Subsystem(nameof(InspectorWindow))]
public sealed class LandscapeChunkInspector : EntityInspector<LandscapeChunk>
{
    // Everything here is computed from the entities that shape the chunk — nothing to filter.
    public bool ShowFieldFilter => false;

    private readonly EditorContext _context;

    public LandscapeChunkInspector(InspectorWindow window)
    {
        _context = window.Context;
    }

    protected override void DrawTargets(InspectorContext context, IReadOnlyList<LandscapeChunk> targets)
    {
        if (targets.Count > 1)
        {
            ImGui.Text($"{targets.Count} chunks selected");
            return;
        }

        LandscapeChunk chunk = targets[0];
        LandscapeChunkOutput output = chunk.Output;

        ImGui.Text(chunk.DisplayName);
        ImGui.TextDisabled("Derived — edit the entities that shape it.");
        ImGui.Separator();

        int limit = _context.Landscape.Settings?.TextureLimit ?? 0;
        Vector4 colour = output.Layers.Count > limit
            ? new Vector4(1.0f, 0.45f, 0.4f, 1.0f)
            : new Vector4(0.42f, 0.85f, 0.46f, 1.0f);
        ImGui.TextColored(colour, $"{output.Layers.Count} / {limit} texture slots");

        int holeCells = 0;
        foreach (bool hole in output.Holes)
        {
            if (hole)
            {
                holeCells++;
            }
        }

        ImGui.TextDisabled(
            $"{output.HeightResolution}² vertices · {output.AlphaResolution}² alpha texels · " +
            $"{holeCells} / {output.Holes.Length} hole cells cut");
        ImGui.Separator();

        for (int i = 0; i < output.Layers.Count; i++)
        {
            LandscapeChunkLayer layer = output.Layers[i];
            string role = layer.IsBase ? "base" : "alpha";
            ImGui.BulletText($"[{i}] {layer.Material?.Name ?? "(fallback missing)"} — {role}");
        }
    }
}
