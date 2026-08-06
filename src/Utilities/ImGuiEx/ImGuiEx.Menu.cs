using System;
using ImGuiNET;

namespace WorldMapStudio;

public static partial class ImGuiEx
{
    public static bool Menu(string label, Action content)
    {
        if (ImGui.BeginMenu(label))
        {
            using (new Scoped(ImGui.EndMenu))
            {
                content();
            }
            return true;
        }
        return false;
    }
}