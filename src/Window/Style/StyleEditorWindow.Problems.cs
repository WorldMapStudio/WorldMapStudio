using System.Collections.Generic;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>The Problems tab: every <see cref="StyleProblem"/> found resolving the active style's
/// chain, plus font load failures from the last atlas rebuild.</summary>
public sealed partial class StyleEditorWindow
{
    private void DrawProblemsTab()
    {
        IReadOnlyList<StyleProblem> problems = EditorStyle.Problems;
        if (problems.Count == 0)
        {
            ImGuiEx.TextColored(CommonColors.Success, "No problems in the active style's chain.");
            return;
        }

        if (!ImGui.BeginTable("StyleProblemsTable", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.Resizable))
        {
            return;
        }

        ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthFixed, 220f);
        ImGui.TableSetupColumn("Message");
        ImGui.TableHeadersRow();

        foreach (StyleProblem problem in problems)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGuiEx.TextColored(CommonColors.Warning, problem.Path);
            ImGui.TableNextColumn();
            ImGui.TextWrapped(problem.Message);
        }

        ImGui.EndTable();
    }
}
