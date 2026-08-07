using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Hosts the editor's tool windows as <see cref="ISubsystem"/>s. Windows declare
/// [Subsystem(nameof(WindowManager))] and are constructed automatically by the generated
/// InitializeSubsystems(), replacing manual registration of each window in <see cref="Editor"/>.
/// </summary>
public sealed partial class WindowManager : ISubsystemHost
{
    public Node3D Root { get; }

    public IEnumerable<Window> Windows => Subsystems.Cast<Window>();

    public WindowManager(Node3D root)
    {
        Root = root;
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
}
