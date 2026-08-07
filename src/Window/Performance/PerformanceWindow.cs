using Godot;
using ImGuiNET;

namespace WorldMapStudio;

[Subsystem(nameof(WindowManager))]
public sealed class PerformanceWindow(WindowManager manager) : Window("Performance")
{
    protected override void DrawContent()
    {
        ImGui.Text($"FPS: {Engine.GetFramesPerSecond():F1}");
    }
}
