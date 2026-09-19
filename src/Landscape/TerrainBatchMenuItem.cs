using ImGuiNET;

namespace WorldMapStudio;

/// <summary>The View menu's terrain batch size, in chunks.</summary>
[Subsystem(nameof(ViewMenu))]
public sealed class TerrainBatchMenuItem : IMenuItem
{
    private readonly EditorContext _context;

    public float Priority => 1f;

    public int Section => 1;

    public TerrainBatchMenuItem(ViewMenu menu)
    {
        _context = menu.Context;
    }

    public void Draw()
    {
        ViewSettings view = _context.View;
        int terrainBatchChunks = view.TerrainBatchChunks;
        ImGui.SetNextItemWidth(120.0f);
        if (ImGui.DragInt("Terrain Batch", ref terrainBatchChunks, 0.1f, 1, LandscapeTerrainBatch.MaxTerrainBatchChunks) &&
            terrainBatchChunks != view.TerrainBatchChunks)
        {
            view.TerrainBatchChunks = terrainBatchChunks;
            _context.Streaming.ReloadTerrain();
        }
    }
}
