using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Read-only inspector for a terrain batch. Everything it shows is computed, so there is nothing to
/// edit here — what it offers instead is why the terrain under it looks the way it does: per chunk,
/// which materials took which slots.
/// </summary>
[Subsystem(nameof(InspectorWindow))]
public sealed class LandscapeTerrainBatchInspector : EntityInspector<LandscapeTerrainBatch>
{
    // Everything here is computed from the entities that shape the terrain — nothing to filter.
    public bool ShowFieldFilter => false;

    private readonly EditorContext _context;

    public LandscapeTerrainBatchInspector(InspectorWindow window)
    {
        _context = window.Context;
    }

    protected override void DrawTargets(InspectorContext context, IReadOnlyList<LandscapeTerrainBatch> targets)
    {
        if (targets.Count > 1)
        {
            ImGui.Text($"{targets.Count} terrain batches selected");
            return;
        }

        LandscapeTerrainBatch batch = targets[0];

        ImGui.Text(batch.DisplayName);
        ImGui.TextDisabled("Derived — edit the entities that shape it.");
        ImGui.TextDisabled($"{batch.Chunks.Count} chunks · {batch.BatchChunks}² grouping");
        ImGui.Separator();

        int limit = _context.Landscape.Settings?.TextureLimit ?? 0;

        foreach ((ChunkCoord coord, LandscapeChunkOutput output) in batch.Chunks
                     .OrderBy(pair => pair.Key.Y)
                     .ThenBy(pair => pair.Key.X)
                     .Select(pair => (pair.Key, pair.Value)))
        {
            Vector4 colour = output.Layers.Count > limit
                ? new Vector4(1.0f, 0.45f, 0.4f, 1.0f)
                : new Vector4(0.42f, 0.85f, 0.46f, 1.0f);

            if (!ImGui.TreeNode($"Chunk {coord}##{coord.X}_{coord.Y}"))
            {
                continue;
            }

            ImGui.TextColored(colour, $"{output.Layers.Count} / {limit} texture slots");
            for (int i = 0; i < output.Layers.Count; i++)
            {
                LandscapeChunkLayer layer = output.Layers[i];
                string role = layer.IsBase ? "base" : "alpha";
                ImGui.BulletText($"[{i}] {layer.Material?.Name ?? "(fallback missing)"} — {role}");
            }

            ImGui.TreePop();
        }
    }
}
