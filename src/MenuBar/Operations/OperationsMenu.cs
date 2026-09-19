using System.Linq;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The "Operations" menu: bulk edits that act on the current selection. Self-registers with
/// <see cref="MenuBarManager"/>, sitting between View and Scene. Each entry is itself an
/// <see cref="IOperation"/> subsystem, declared with [Subsystem(nameof(OperationsMenu))].
/// </summary>
[Subsystem(nameof(MenuBarManager))]
[SubsystemHost(typeof(IOperation))]
public sealed partial class OperationsMenu : IMainMenu, ISubsystemHost
{
    /// <summary>The editor's shared systems, forwarded down to hosted operations.</summary>
    public EditorContext Context { get; }

    public float Priority => 0.7f;

    public OperationsMenu(MenuBarManager manager)
    {
        Context = manager.Context;
        InitializeSubsystems();
    }

    public void Draw()
    {
        ImGuiEx.Menu("Operations", () =>
        {
            foreach (IOperation operation in Subsystems)
            {
                if (ImGui.MenuItem(operation.Name, operation.ShortcutLabel, false, operation.CanApply()))
                {
                    operation.Apply();
                }
            }
        });
    }
}
