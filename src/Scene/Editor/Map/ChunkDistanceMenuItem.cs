using ImGuiNET;

namespace WorldMapStudio;

/// <summary>The View menu's streaming distance, in chunks.</summary>
[Subsystem(nameof(ViewMenu))]
public sealed class ChunkDistanceMenuItem : IMenuItem
{
    private readonly EditorContext _context;

    public float Priority => 0f;

    public int Section => 1;

    public ChunkDistanceMenuItem(ViewMenu menu)
    {
        _context = menu.Context;
    }

    public void Draw()
    {
        ViewSettings view = _context.View;
        int viewDistanceChunks = view.ViewDistanceChunks;
        ImGui.SetNextItemWidth(120.0f);
        if (ImGui.DragInt("Chunk Distance", ref viewDistanceChunks, 0.1f, 1, 64) &&
            viewDistanceChunks != view.ViewDistanceChunks)
        {
            view.ViewDistanceChunks = viewDistanceChunks;

            // Streaming reads this only when a scan starts, and a scan is otherwise gated on the
            // focus having moved — without this the new distance does nothing until the camera
            // travels far enough to trigger a rescan on its own.
            _context.Streaming.Invalidate();
        }
    }
}
