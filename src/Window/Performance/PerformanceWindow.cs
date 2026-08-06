using Godot;
using ImGuiNET;

namespace WorldMapStudio;

public sealed class PerformanceWindow() : ImGuiWindow("Performance")
{
    protected override void DrawContent()
    {
        ImGui.Text($"FPS: {Engine.GetFramesPerSecond():F1}");
    }
}
