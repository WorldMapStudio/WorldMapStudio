using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Modal for editing an existing project's settings in place (currently just its coordinate
/// convention).
/// </summary>
public sealed class EditProjectSettingsOperation : IModalOperation<Project>
{
    public ModalOperationState Draw(Project context)
    {
        ImGui.Text(context.Name);
        ImGui.TextDisabled("Project Settings");
        ImGui.Separator();

        ImGui.TextDisabled("Coordinate System");
        AxisConventionEditor.Draw(context.AxisConvention);

        ImGui.Separator();

        var state = ModalOperationState.Running;
        if (ImGui.Button("Close", new Vector2(120, 0)))
        {
            state = ModalOperationState.Confirmed;
        }

        return state;
    }
}
