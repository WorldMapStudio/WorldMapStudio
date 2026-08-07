#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// Lists the in-memory projects and lets the user create one (by name), open one into the
/// <see cref="Editor"/>, or remove it. There is no persistence yet, so the list starts empty each run.
/// </summary>
public sealed class ProjectSelect : IScene
{
    private readonly Node3D _root;
    private readonly List<Project> _projects = [];
    private string _newProjectName = "";

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

            ImGuiEx.Child("ProjectList", new Vector2(0, -70), true, ImGuiWindowFlags.None, () =>
            {
                foreach (var project in _projects.ToArray())
                {
                    ImGui.PushID(project.Name);

                    ImGui.Text(project.Name);
                    ImGui.SameLine();

                    if (ImGui.Button("Open"))
                    {
                        scene = new Editor(_root, project);
                    }

                    ImGui.SameLine();

                    if (ImGui.Button("Remove"))
                    {
                        _projects.Remove(project);
                    }

                    ImGui.Separator();
                    ImGui.PopID();
                }
            });

            ImGui.SetNextItemWidth(220);
            ImGui.InputText("##NewProjectName", ref _newProjectName, 128);
            ImGui.SameLine();

            var trimmedName = _newProjectName.Trim();
            bool canCreate = trimmedName.Length > 0 && _projects.All(p => p.Name != trimmedName);

            if (!canCreate)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("New Project", new Vector2(150, 30)))
            {
                _projects.Add(new Project { Name = trimmedName });
                _newProjectName = "";
            }

            if (!canCreate)
            {
                ImGui.EndDisabled();
            }

            ImGui.SameLine();

            if (ImGui.Button("Back", new Vector2(150, 30)))
            {
                scene = new MainMenu(_root);
            }
        });

        return scene;
    }
}
