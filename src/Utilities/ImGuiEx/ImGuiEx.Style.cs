using System;
using ImGuiNET;

namespace WorldMapStudio;

public static partial class ImGuiEx
{
    public static void TextColored(StyleColor color, string text) => ImGui.TextColored(color.Value, text);

    public static IDisposable PushStyleColor(ImGuiCol idx, StyleColor color)
    {
        ImGui.PushStyleColor(idx, color.Value);
        return new Scoped(() => ImGui.PopStyleColor());
    }

    /// <summary>Pushes <paramref name="font"/>'s current <see cref="StyleFont.Pointer"/>. A no-op
    /// scope if the atlas hasn't bound the slot yet (before the first frame, or a load failure) —
    /// callers get ImGui's currently active font instead of a crash.</summary>
    public static unsafe IDisposable PushFont(StyleFont font)
    {
        if (font.Pointer.NativePtr == null)
        {
            return new Scoped(() => { });
        }

        ImGui.PushFont(font.Pointer);
        return new Scoped(ImGui.PopFont);
    }
}
