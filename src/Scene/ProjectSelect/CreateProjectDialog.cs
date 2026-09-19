#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Modal for creating a new project: a name, its initial coordinate convention, and its database
/// connection. On confirm it exposes the built <see cref="Project"/> via <see cref="CreatedProject"/>.
/// </summary>
public sealed class CreateProjectOperation : IModalOperation<IReadOnlyList<Project>>
{
    private string _name = "";
    private string? _error;
    private readonly AxisConvention _axes = AxisConvention.GodotDefault;
    private readonly StorageConnection _database = EditorStorage.DefaultConnection();
    private readonly List<AssetSourceSettings> _assetSources = [];
    private readonly AssetSourceEditor _assetSourceEditor = new();

    public Project? CreatedProject { get; private set; }

    public ModalOperationState Draw(IReadOnlyList<Project> existingProjects)
    {
        ImGui.Text("New Project");
        ImGui.Separator();

        ImGui.InputText("Name", ref _name, 128);

        ImGui.Spacing();
        ImGui.TextDisabled("Coordinate System");
        ImGui.Separator();
        AxisConventionEditor.Draw(_axes);

        ImGui.Spacing();
        ImGui.TextDisabled("Database");
        ImGui.Separator();
        StorageConnectionEditor.Draw(_database);

        ImGui.Spacing();
        ImGui.TextDisabled("Assets");
        ImGui.Separator();
        _assetSourceEditor.Draw(_assetSources);

        if (_error != null)
        {
            ImGuiEx.TextColored(CommonColors.Error, _error);
        }

        ImGui.Separator();

        var state = ModalOperationState.Running;

        if (ImGui.Button("Create", new Vector2(120, 0)))
        {
            if (TryCreate(existingProjects, out string? error))
            {
                state = ModalOperationState.Confirmed;
            }
            else
            {
                _error = error;
            }
        }

        ImGui.SameLine();

        if (ImGui.Button("Cancel", new Vector2(120, 0)))
        {
            state = ModalOperationState.Cancelled;
        }

        return state;
    }

    private bool TryCreate(IReadOnlyList<Project> existingProjects, out string? error)
    {
        string name = _name.Trim();

        if (name.Length == 0)
        {
            error = "Name is required.";
            return false;
        }

        if (existingProjects.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            error = "A project with that name already exists.";
            return false;
        }

        CreatedProject = new Project
        {
            Name = name,
            AxisConvention = _axes,
        };
        CreatedProject.StorageConnections[EditorStorage.StorageName] = _database;
        foreach (AssetSourceSettings source in _assetSources)
        {
            CreatedProject.AssetSources.Add(source);
        }

        error = null;
        return true;
    }
}
