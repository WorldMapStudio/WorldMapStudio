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

    public Editor(Node3D root, Project project)
    {
        _root = root;
        _project = project;
    }

    public void Start()
    {
        _windowManager = new WindowManager(_root);
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

            ImGuiEx.Menu("Window", () => _windowManager.DrawMenuItems());

            ImGui.Separator();
            ImGui.TextDisabled(_project.Name);
        });

        ImGui.DockSpaceOverViewport();

        _windowManager.Draw();

        return scene;
    }
}
