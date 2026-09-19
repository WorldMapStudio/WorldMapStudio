using ImGuiNET;

namespace WorldMapStudio;

/// <summary>"Clean up deleted maps' data…": opens the stray map data cleanup dialog.</summary>
[Subsystem(nameof(MapMenu))]
public sealed class StrayMapCleanupMenuItem : IMenuItem
{
    private readonly EditorContext _context;
    private readonly ModalOperator<StrayMapCleanupOperation, MapSystem> _modal = new("StrayMapCleanup", () => new StrayMapCleanupOperation());

    public float Priority => 2f;

    public int Section => 1;

    public StrayMapCleanupMenuItem(MapMenu menu)
    {
        _context = menu.Context;
    }

    public void Draw()
    {
        if (ImGui.MenuItem("Clean up deleted maps' data…"))
        {
            _modal.Show();
        }
    }

    public void DrawOverlay() => _modal.Draw(_context.Maps, true, ImGuiWindowFlags.None);
}
