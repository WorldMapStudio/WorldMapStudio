using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Hosts the editor's tool windows as <see cref="ISubsystem"/>s. Windows declare
/// [Subsystem(nameof(WindowManager))] and are constructed automatically by the generated
/// InitializeSubsystems(), replacing manual registration of each window in <see cref="Editor"/>.
/// WindowManager is itself a top-level menu, self-registered with <see cref="MenuBarManager"/>.
/// </summary>
[Subsystem(nameof(MenuBarManager))]
public sealed partial class WindowManager : ISubsystemHost, IMainMenu
{
    public float Priority => 1f;

    public Node3D Root { get; }

    public IEnumerable<Window> Windows => Subsystems.Cast<Window>();

    public WindowManager(MenuBarManager manager)
    {
        Root = manager.Root;
        InitializeSubsystems();
    }

    public void Draw()
    {
        foreach (Window window in Windows)
        {
            window.Draw();
        }
    }

    public void DrawMenuItems()
    {
        foreach (Window window in Windows)
        {
            window.DrawMenuItem();
        }
    }

    void IMainMenu.Draw() => ImGuiEx.Menu("Window", DrawMenuItems);
}
