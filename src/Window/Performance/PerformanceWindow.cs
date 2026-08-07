using Godot;
using ImGuiNET;

namespace WorldMapStudio;

[Subsystem(nameof(WindowManager))]
public sealed class PerformanceWindow : Window
{
    public PerformanceWindow(WindowManager manager) : base("Performance")
    {
    }

    protected override void DrawContent()
    {
        ImGui.Text($"FPS: {Engine.GetFramesPerSecond():F1}");
    }
}
