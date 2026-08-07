#nullable enable
using System.Collections.Generic;
using Godot;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The editor scene: a docked ImGui workspace of tool windows over the 3D viewport. This is the
/// content that used to live directly on the root <c>WorldMapStudio</c> node, now hosted behind
/// <see cref="IScene"/> so the app can route through a main menu and project selection first.
/// </summary>
public sealed class Editor : IScene
{
    private readonly Node3D _root;
    private readonly Project _project;
    private readonly List<ImGuiWindow> _windows = [];

    public Editor(Node3D root, Project project)
    {
        _root = root;
        _project = project;
    }

    public void Start()
    {
        _windows.Add(new PerformanceWindow());
        _windows.Add(new ComputeMaterialWindow());
        _windows.Add(new ViewportWindow(_root));
        _windows.Add(new WorkQueueWindow());
        _windows.Add(new WorkTestWindow());
        _windows.Add(new TestRunnerWindow(_root));
    }

    public IScene? Update()
    {
        IScene? scene = this;

        ImGuiEx.MainMenuBar(() =>
        {
            ImGuiEx.Menu("File", () =>
            {
                if (ImGui.MenuItem("Exit"))
                {
                    scene = null;
                }
            });

            ImGuiEx.Menu("Window", () =>
            {
                foreach (ImGuiWindow window in _windows)
                {
                    window.DrawMenuItem();
                }
            });

            ImGui.Separator();
            ImGui.TextDisabled(_project.Name);
        });

        ImGui.DockSpaceOverViewport();

        foreach (ImGuiWindow window in _windows)
        {
            window.Draw();
        }

        return scene;
    }
}
