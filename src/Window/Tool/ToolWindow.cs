using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Lists the available tools and lets the user pick the active one, like Blender's tool shelf.
/// Tool factories self-register here with [Subsystem(nameof(ToolWindow))]; this window hands them to
/// the shared <see cref="ToolSystem"/>, which owns the active tool the viewport drives.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed partial class ToolWindow : Window, ISubsystemHost
{
    private readonly ToolSystem _tools;

    public ToolWindow(WindowManager manager)
        : base("Tools", defaultSize: new Vector2(200, 300))
    {
        _tools = manager.Tools;
        InitializeSubsystems();

        foreach (IToolFactory factory in Subsystems.Cast<IToolFactory>())
        {
            _tools.Register(factory);
        }

        _tools.EnsureActive();
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
