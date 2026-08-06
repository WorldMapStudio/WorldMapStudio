using System;
using ImGuiNET;

namespace WorldMapStudio;

public static partial class ImGuiEx
{
    public static void MainMenuBar(Action content)
    {
        if (ImGui.BeginMainMenuBar())
        {
            using (new Scoped(ImGui.EndMenuBar))
            {
                content();
            }
        }
    }
}