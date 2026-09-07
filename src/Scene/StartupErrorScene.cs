#nullable enable
using Godot;
using ImGuiNET;
using Vector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// A dead-end scene shown when startup cannot proceed — currently only when a project config passed
/// on the command line fails to load. It offers nothing but the reason and an exit, so a broken
/// launch configuration is impossible to mistake for a normal start.
/// </summary>
public sealed class StartupErrorScene : IScene
{
    private static readonly Vector4 ErrorColor = new(0.9f, 0.4f, 0.4f, 1f);

    private readonly string _caption;
    private readonly string _detail;

    public StartupErrorScene(string caption, string detail)
    {
        _caption = caption;
        _detail = detail;
    }

    public void Start()
    {
    }

    public IScene? Update()
    {
        IScene? scene = this;

        ImGuiEx.FullScreen("StartupError", ImGuiWindowFlags.None, () =>
        {
            ImGuiEx.Center(560, 30, 12, center =>
            {
                center.Label(_caption);
                center.LabelColored(_detail, ErrorColor);
                center.Button("Exit", () => scene = null);
            });
        });

        return scene;
    }
}
