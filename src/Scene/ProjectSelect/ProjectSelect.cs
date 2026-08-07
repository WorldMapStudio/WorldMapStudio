#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// Lists the in-memory projects and lets the user create one (name + coordinate convention),
/// edit an existing project's settings, open one into the <see cref="Editor"/>, or remove it
/// (behind a confirmation). There is no persistence yet, so the list starts empty each run.
/// </summary>
public sealed class ProjectSelect : IScene
{
    private readonly Node3D _root;
    private readonly List<Project> _projects = [];

    private Project? _settingsProject;
    private Project? _pendingDelete;
    private ModalConfirm? _deleteConfirm;

    private readonly ModalOperator<CreateProjectOperation, IReadOnlyList<Project>> _createModal =
        new("CreateProject", () => new CreateProjectOperation(), new Vector2(360, 0));

    private readonly ModalOperator<EditProjectSettingsOperation, Project> _settingsModal =
        new("ProjectSettings", () => new EditProjectSettingsOperation(), new Vector2(360, 0));

    public ProjectSelect(Node3D root)
    {
        _root = root;
    }

    public void Start()
    {
    }

    public IScene? Update()
    {
        IScene? scene = this;

        ImGuiEx.FullScreen("ProjectSelect", ImGuiWindowFlags.None, () =>
        {
            ImGui.Text("Projects");
            ImGui.Separator();

            ImGuiEx.Child("ProjectList", new Vector2(0, -40), true, ImGuiWindowFlags.None, () =>
            {
                if (_projects.Count == 0)
                {
                    ImGui.TextDisabled("No projects yet.");
                }

                foreach (var project in _projects.ToArray())
                {
                    ImGui.PushID(project.Name);

                    ImGui.Text(project.Name);
                    ImGui.SameLine();
                    ImGui.TextDisabled(ConventionSummary(project.AxisConvention));

                    if (ImGui.Button("Open"))
                    {
                        scene = new Editor(_root, project);
                    }

                    ImGui.SameLine();

                    if (ImGui.Button("Settings"))
                    {
                        _settingsProject = project;
                        _settingsModal.Show();
                    }

                    ImGui.SameLine();

                    if (ImGui.Button("Remove"))
                    {
                        _pendingDelete = project;
                        _deleteConfirm = new ModalConfirm(
                            "Remove Project",
                            $"Remove '{project.Name}' from the project list?",
                            "Remove",
                            "Cancel");
                        _deleteConfirm.Show();
                    }

                    ImGui.Separator();
                    ImGui.PopID();
                }
            });

            if (ImGui.Button("New Project", new Vector2(150, 30)))
            {
                _createModal.Show();
            }

            ImGui.SameLine();

            if (ImGui.Button("Back", new Vector2(150, 30)))
            {
                scene = new MainMenu(_root);
            }

            DrawCreateModal();
            DrawSettingsModal();
            DrawDeleteConfirm();
        });

        return scene;
    }

    private void DrawCreateModal()
    {
        var createOperation = _createModal._item;
        if (_createModal.Draw(_projects, true, ImGuiWindowFlags.None) == ModalOperationState.Confirmed)
        {
            var created = createOperation?.CreatedProject;
            if (created != null && _projects.All(p => p.Name != created.Name))
            {
                _projects.Add(created);
            }
        }
    }

    private void DrawSettingsModal()
    {
        if (_settingsProject == null)
        {
            return;
        }

        var state = _settingsModal.Draw(_settingsProject, true, ImGuiWindowFlags.None);
        if (state is ModalOperationState.Confirmed or ModalOperationState.Cancelled)
        {
            _settingsProject = null;
        }
    }

    private void DrawDeleteConfirm()
    {
        if (_deleteConfirm == null)
        {
            return;
        }

        var state = _deleteConfirm.Draw(true);
        if (state == ModalOperationState.Confirmed && _pendingDelete != null)
        {
            _projects.Remove(_pendingDelete);
        }

        if (state is ModalOperationState.Confirmed or ModalOperationState.Cancelled)
        {
            _deleteConfirm = null;
            _pendingDelete = null;
        }
    }

    private static string ConventionSummary(AxisConvention axes) =>
        axes.IsGodotDefault
            ? "Godot axes"
            : $"X {axes.X.Label()}, Y {axes.Y.Label()}, Z {axes.Z.Label()}";
}
