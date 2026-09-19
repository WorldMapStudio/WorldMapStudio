using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Lists the available tools and lets the user pick the active one, like Blender's tool shelf.
/// The tools themselves live on the shared <see cref="ToolSystem"/>, which owns the active tool the
/// viewport drives.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class ToolWindow : Window
{
    public override KeyboardShortcut DefaultShortcut => new(ImGuiKey.T, ShortcutModifiers.Alt);

    private readonly ToolSystem _tools;

    public ToolWindow(WindowManager manager)
        : base("Tools", defaultSize: new Vector2(200, 300))
    {
        _tools = manager.Context.Tools;
    }

    protected override void DrawContent()
    {
        foreach (IToolFactory factory in _tools.Factories)
        {
            bool active = ReferenceEquals(factory, _tools.ActiveFactory);
            bool enabled = factory.CanActivate();

            if (!enabled)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.RadioButton(factory.Name, active) && !active)
            {
                _tools.Activate(factory);
            }

            if (!enabled)
            {
                ImGui.EndDisabled();
            }
        }
    }
}
