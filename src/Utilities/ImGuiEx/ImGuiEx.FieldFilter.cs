using ImGuiNET;

namespace WorldMapStudio;

public static partial class ImGuiEx
{
    /// <summary>The full-width "filter fields" box that feeds a <see cref="FieldFilter"/>. The caller
    /// keeps <paramref name="text"/> across frames and builds a fresh <see cref="FieldFilter"/> from it
    /// each frame.</summary>
    public static bool FieldFilterInput(string id, ref string text, string hint = "Filter fields...")
    {
        ImGui.SetNextItemWidth(-1.0f);
        return ImGui.InputTextWithHint(id, hint, ref text, 128);
    }
}
