using System.Collections.Generic;
using System.Linq;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>Draws a project's named paths as a key/value table with add and remove.</summary>
public sealed class ProjectPathsEditor
{
    private string _newKey = string.Empty;

    public void Draw(IDictionary<string, string> paths)
    {
        foreach (KeyValuePair<string, string> entry in paths.OrderBy(p => p.Key).ToList())
        {
            ImGui.PushID(entry.Key);

            string value = entry.Value;
            if (ImGui.InputText(entry.Key, ref value, 512))
            {
                paths[entry.Key] = value;
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Remove"))
            {
                paths.Remove(entry.Key);
            }

            ImGui.PopID();
        }

        if (paths.Count == 0)
        {
            ImGui.TextDisabled("No paths configured.");
        }

        ImGui.InputText("##newPathKey", ref _newKey, 128);
        ImGui.SameLine();

        string key = _newKey.Trim();
        ImGui.BeginDisabled(key.Length == 0 || paths.ContainsKey(key));
        if (ImGui.SmallButton("Add path"))
        {
            paths[key] = string.Empty;
            _newKey = string.Empty;
        }

        ImGui.EndDisabled();
    }
}
