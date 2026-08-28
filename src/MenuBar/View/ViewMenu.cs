using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The "View" menu: toggles for viewport display options such as the grid. Self-registers with
/// <see cref="MenuBarManager"/>, sitting between Edit and Scene.
/// </summary>
[Subsystem(nameof(MenuBarManager))]
public sealed class ViewMenu : IMainMenu
{
    private readonly ViewSettings _view;
    private readonly ShortcutAction _grid;
    private readonly ShortcutAction _chunkEdges;

    public float Priority => 0.6f;

    public ViewMenu(MenuBarManager manager)
    {
        _view = manager.Context.View;
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
        });
    }
}
