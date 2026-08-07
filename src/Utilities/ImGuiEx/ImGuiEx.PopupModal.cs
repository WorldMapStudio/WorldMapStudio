using System;
using Godot;
using ImGuiNET;

namespace WorldMapStudio;

public static partial class ImGuiEx
{
    /// <summary>
    /// Version of PopupModal in callback style.
    /// </summary>
    /// <param name="canBeClosed">If true, the popup can be closed with a shortcut.</param>
    /// <param name="content">Action to invoke for drawing modal content.</param>
    public static bool PopupModal(string id, bool canBeClosed, ref bool open, ImGuiWindowFlags flags, Action content)
    {
        if (canBeClosed && Input.IsKeyPressed(Key.Escape))
        {
            open = false;
            return false;
        }

        if (!ImGui.BeginPopupModal(id, ref open, flags))
        {
            return false;
        }

        using (new Scoped(ImGui.EndPopup))
        {
            content();
        }
        return true;
    }
}