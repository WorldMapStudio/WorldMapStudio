using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Modal for editing an existing project's settings in place: its coordinate convention, its
/// database connection, its asset sources and its named paths.
/// </summary>
public sealed class EditProjectSettingsDialog : IModalDialog<Project>
{
    private readonly AssetSourceEditor _assetSourceEditor = new();
    private readonly ProjectPathsEditor _pathsEditor = new();

    public ModalDialogState Draw(Project context)
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

        ImGui.Spacing();
        ImGui.TextDisabled("Assets");
        _assetSourceEditor.Draw(context.AssetSources);

        ImGui.Spacing();
        ImGui.TextDisabled("Paths");
        _pathsEditor.Draw(context.Paths);

        ImGui.Separator();

        var state = ModalDialogState.Running;
        if (ImGui.Button("Close", new Vector2(120, 0)))
        {
            state = ModalDialogState.Confirmed;
        }

        return state;
    }
}
