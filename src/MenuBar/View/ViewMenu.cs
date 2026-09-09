using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The "View" menu: toggles and settings for viewport display, such as the grid and streaming
/// distance. Self-registers with <see cref="MenuBarManager"/>, sitting between Edit and Scene.
/// </summary>
[Subsystem(nameof(MenuBarManager))]
public sealed class ViewMenu : IMainMenu
{
    private readonly ViewSettings _view;
    private readonly EditorContext _context;
    private readonly ShortcutAction _grid;
    private readonly ShortcutAction _chunkEdges;
    private readonly ShortcutAction _environmentLighting;
    private readonly ShortcutAction _environmentVolumes;

    public float Priority => 0.6f;

    public ViewMenu(MenuBarManager manager)
    {
        _view = manager.Context.View;
        _context = manager.Context;
        _grid = manager.Context.Shortcuts.Register(
            "view.grid",
            "View",
            "Grid",
            new KeyboardShortcut(ImGuiKey.G, ShortcutModifiers.Alt),
            () => _view.ShowGrid = !_view.ShowGrid);
        _chunkEdges = manager.Context.Shortcuts.Register(
            "view.chunk-edges",
            "View",
            "Chunk Edges",
            new KeyboardShortcut(ImGuiKey.E, ShortcutModifiers.Alt),
            () => _view.ShowChunkEdges = !_view.ShowChunkEdges);
        _environmentLighting = manager.Context.Shortcuts.Register(
            "view.environment-lighting",
            "View",
            "Environment Lighting",
            new KeyboardShortcut(ImGuiKey.M, ShortcutModifiers.Alt),
            () => _view.UseEnvironmentLighting = !_view.UseEnvironmentLighting);
        _environmentVolumes = manager.Context.Shortcuts.Register(
            "view.environment-volumes",
            "View",
            "Environment Volumes",
            KeyboardShortcut.None,
            () => _view.ShowEnvironmentVolumes = !_view.ShowEnvironmentVolumes);
    }

    public void Draw()
    {
        ImGuiEx.Menu("View", () =>
        {
            bool showGrid = _view.ShowGrid;
            if (ImGui.MenuItem("Grid", _grid.ShortcutLabel, ref showGrid))
            {
                _view.ShowGrid = showGrid;
            }

            bool showChunkEdges = _view.ShowChunkEdges;
            if (ImGui.MenuItem("Chunk Edges", _chunkEdges.ShortcutLabel, ref showChunkEdges))
            {
                _view.ShowChunkEdges = showChunkEdges;
            }

            bool useEnvironmentLighting = _view.UseEnvironmentLighting;
            if (ImGui.MenuItem("Environment Lighting", _environmentLighting.ShortcutLabel, ref useEnvironmentLighting))
            {
                _view.UseEnvironmentLighting = useEnvironmentLighting;
            }

            bool showEnvironmentVolumes = _view.ShowEnvironmentVolumes;
            if (ImGui.MenuItem("Environment Volumes", _environmentVolumes.ShortcutLabel, ref showEnvironmentVolumes))
            {
                _view.ShowEnvironmentVolumes = showEnvironmentVolumes;
            }

            ImGui.Separator();

            int viewDistanceChunks = _view.ViewDistanceChunks;
            ImGui.SetNextItemWidth(120.0f);
            if (ImGui.DragInt("Chunk Distance", ref viewDistanceChunks, 0.1f, 1, 64) &&
                viewDistanceChunks != _view.ViewDistanceChunks)
            {
                _view.ViewDistanceChunks = viewDistanceChunks;

                // Streaming reads this only when a scan starts, and a scan is otherwise gated on the
                // focus having moved — without this the new distance does nothing until the camera
                // travels far enough to trigger a rescan on its own.
                _context.Streaming.Invalidate();
            }

            int terrainBatchChunks = _view.TerrainBatchChunks;
            ImGui.SetNextItemWidth(120.0f);
            if (ImGui.DragInt("Terrain Batch", ref terrainBatchChunks, 0.1f, 1, LandscapeTerrainBatch.MaxTerrainBatchChunks) &&
                terrainBatchChunks != _view.TerrainBatchChunks)
            {
                _view.TerrainBatchChunks = terrainBatchChunks;
                _context.Streaming.ReloadTerrain();
            }
        });
    }
}
