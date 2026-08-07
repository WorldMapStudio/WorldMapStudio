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
    private WindowManager _windowManager = null!;
    private FileMenuManager _menuManager = null!;

    public Editor(Node3D root, Project project)
    {
        _root = root;
        _project = project;
    }

    public void Start()
    {
        _windowManager = new WindowManager(_root);
        _menuManager = new FileMenuManager();
    }

    public IScene? Update()
    {
        ImGuiEx.MainMenuBar(() =>
        {
            _menuManager.Draw();
            ImGuiEx.Menu("Window", () => _windowManager.DrawMenuItems());

            ImGui.Separator();
            ImGui.TextDisabled(_project.Name);
        });

        ImGui.DockSpaceOverViewport();

        _windowManager.Draw();

        return _menuManager.ExitRequested ? null : this;
    }
}
