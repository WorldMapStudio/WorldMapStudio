using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Draws the editable fields of a <see cref="StorageLocation"/>: the DoltLite file path and the
/// branch to open it on. Returns true on the frames any field changed.
/// </summary>
public static class StorageLocationEditor
{
    public static bool Draw(StorageLocation location)
    {
        bool changed = false;

        string path = location.DatabasePath;
        if (ImGui.InputText("Database File", ref path, 512))
        {
            location.DatabasePath = path;
            changed = true;
        }

        string branch = location.Branch;
        if (ImGui.InputText("Branch", ref branch, 256))
        {
            location.Branch = branch;
            changed = true;
        }

        return changed;
    }
}
