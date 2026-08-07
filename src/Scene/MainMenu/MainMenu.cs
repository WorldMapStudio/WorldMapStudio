#nullable enable
using Godot;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The first scene shown on launch: a centered title with buttons to open a project or quit.
/// </summary>
public sealed class MainMenu : IScene
{
    private readonly Node3D _root;

    public MainMenu(Node3D root)
    {
        _root = root;
    }

    public void Start()
    {
    }

    public IScene? Update()
    {
        IScene? scene = this;

        ImGuiEx.FullScreen("MainMenu", ImGuiWindowFlags.None, () =>
        {
            ImGuiEx.Center(200, 50, 15, center =>
            {
                center.Label("WorldMapStudio");
                center.Button("Open Project", () => scene = new ProjectSelect(_root));
                center.Button("Exit", () => scene = null);
            });
        });

        return scene;
    }
}
