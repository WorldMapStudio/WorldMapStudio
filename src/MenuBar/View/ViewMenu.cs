using System.Collections.Generic;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The "View" menu: toggles and settings for viewport display, such as the grid and streaming
/// distance, plus every registered <see cref="IViewCategory"/> as a per-type show/hide toggle.
/// Self-registers with <see cref="MenuBarManager"/>, sitting between Edit and Scene.
/// </summary>
[Subsystem(nameof(MenuBarManager))]
public sealed class ViewMenu : IMainMenu
{
    private readonly ViewSettings _view;
    private readonly EditorContext _context;
    private readonly ViewCategorySystem _viewCategories;
    private readonly ShortcutAction _grid;
    private readonly ShortcutAction _chunkEdges;
    private readonly ShortcutAction _environmentVolumes;
    private readonly List<(IViewCategory Category, ShortcutAction Shortcut)> _categoryShortcuts = [];

    public float Priority => 0.6f;

    public ViewMenu(MenuBarManager manager)
    {
        _view = manager.Context.View;
        _context = manager.Context;
        _viewCategories = manager.Context.ViewCategories;
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
        _environmentVolumes = manager.Context.Shortcuts.Register(
            "view.environment-volumes",
            "View",
            "Environment Volumes",
            KeyboardShortcut.None,
            () => _view.ShowEnvironmentVolumes = !_view.ShowEnvironmentVolumes);

        foreach (IViewCategory category in _viewCategories.All)
        {
            string id = category.Id;
            ShortcutAction shortcut = manager.Context.Shortcuts.Register(
                $"view.show.{id}",
                "View",
                category.DisplayName,
                category.DefaultShortcut,
                () => _viewCategories.SetHidden(id, !_viewCategories.IsHidden(id)));
            _categoryShortcuts.Add((category, shortcut));
        }
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

            DrawCategories();
        });
    }

    // Every registered view category, drawn as a checkmarked toggle under its own Group heading, in
    // the Priority order ViewCategorySystem.All already carries — this never names a category, the
    // same way SpawnMenu never names a spawn factory.
    private void DrawCategories()
    {
        if (_categoryShortcuts.Count == 0)
        {
            return;
        }

        ImGui.Separator();

        string? lastGroup = null;
        foreach ((IViewCategory category, ShortcutAction shortcut) in _categoryShortcuts)
        {
            if (category.Group != lastGroup)
            {
                lastGroup = category.Group;
                ImGui.TextDisabled(category.Group);
            }

            bool shown = !_viewCategories.IsHidden(category.Id);
            if (ImGui.MenuItem(category.DisplayName, shortcut.ShortcutLabel, ref shown))
            {
                _viewCategories.SetHidden(category.Id, !shown);
            }
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Show All"))
        {
            _viewCategories.ShowAll();
        }
    }
}
