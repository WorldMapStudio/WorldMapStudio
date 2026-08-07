using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Hosts the top-level entries of the editor's main menu bar (File, Window, ...) as
/// <see cref="ISubsystem"/>s. Entries declare [Subsystem(nameof(MenuBarManager))] and are
/// constructed automatically by the generated InitializeSubsystems(), so a new top-level menu
/// (and everything it hosts) can register itself without touching <see cref="Editor"/>.
/// </summary>
public sealed partial class MenuBarManager : ISubsystemHost
{
    public Node3D Root { get; }

    public MenuBarManager(Node3D root)
    {
        Root = root;
        InitializeSubsystems();
    }

    public void Draw()
    {
        foreach (IMainMenu menu in Subsystems.Cast<IMainMenu>())
        {
            menu.Draw();
        }
    }
}
