using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Modal for editing an existing project's settings in place: its coordinate convention and its
/// database connection.
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

        ImGui.Spacing();
        ImGui.TextDisabled("Database");
        StorageConnection connection = context.GetOrAddStorageConnection(EditorStorage.StorageName, EditorStorage.DefaultConnection());
        StorageConnectionEditor.Draw(connection);

        ImGui.Separator();

        var state = ModalOperationState.Running;
        if (ImGui.Button("Close", new Vector2(120, 0)))
        {
            state = ModalOperationState.Confirmed;
        }

        return state;
    }
}
