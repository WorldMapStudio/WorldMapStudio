using System;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

public static partial class ImGuiEx
{
    public static bool Window(string label, ImGuiWindowFlags flags, Action content)
    {
        using (new Scoped(ImGui.End))
        {
            if (ImGui.Begin(label, flags))
            {
                content();
                return true;
            }
        }
        return false;
    }

    public static bool Child(string label, Vector2 size, bool border, ImGuiWindowFlags flags, Action content)
    {
        using (new Scoped(ImGui.EndChild))
        {
            if (ImGui.BeginChild(label, size, border, flags))
            {
                content();
                return true;
            }
        }
        return false;
    }
}
