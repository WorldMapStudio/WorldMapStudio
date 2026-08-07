#nullable enable
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
    private MenuBarManager _menuBar = null!;

    public Editor(Node3D root, Project project)
    {
        _root = root;
        _project = project;
    }

    public void Start()
    {
        _menuBar = new MenuBarManager(_root, _project);
    }

    public IScene? Update()
    {
        ImGuiEx.MainMenuBar(() =>
        {
            _menuBar.Draw();

            ImGui.Separator();
            ImGui.TextDisabled(_project.Name);
        });

        ImGui.DockSpaceOverViewport();

        _menuBar.WindowManager.Draw();

        return _menuBar.FileMenuManager.ExitRequested ? null : this;
    }
}
