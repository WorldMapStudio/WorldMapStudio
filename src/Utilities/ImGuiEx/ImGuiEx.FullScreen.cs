using System;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

public static partial class ImGuiEx
{
    public static bool FullScreen(string label, ImGuiWindowFlags flags, Action content)
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(viewport.Pos.X, viewport.Pos.Y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(viewport.Size, ImGuiCond.Always);
        return Window(
            label,
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus | flags,
            content
        );
    }
}
