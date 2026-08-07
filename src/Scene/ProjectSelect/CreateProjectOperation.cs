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

        if (_error != null)
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), _error);
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
        error = null;
        return true;
    }
}
