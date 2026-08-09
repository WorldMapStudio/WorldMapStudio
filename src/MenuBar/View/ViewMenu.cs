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

    public float Priority => 0.6f;

    public ViewMenu(MenuBarManager manager)
    {
        _view = manager.Context.View;
    }

    public void Draw()
    {
        ImGuiEx.Menu("View", () =>
        {
            bool showGrid = _view.ShowGrid;
            if (ImGui.MenuItem("Grid", string.Empty, ref showGrid))
            {
                _view.ShowGrid = showGrid;
            }

            bool showChunkEdges = _view.ShowChunkEdges;
            if (ImGui.MenuItem("Chunk Edges", string.Empty, ref showChunkEdges))
            {
                _view.ShowChunkEdges = showChunkEdges;
            }
        });
    }
}
