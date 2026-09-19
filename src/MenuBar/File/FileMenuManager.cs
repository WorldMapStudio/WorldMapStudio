using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Hosts the editor's "File" menu entries as <see cref="ISubsystem"/>s. Items declare
/// [Subsystem(nameof(FileMenuManager))] and are constructed automatically by the generated
/// InitializeSubsystems(), letting features like "Exit" register themselves instead of being
/// wired up by hand in <see cref="Editor"/>. FileMenuManager is itself a top-level menu, self-
/// registered with <see cref="MenuBarManager"/>.
/// </summary>
[Subsystem(nameof(MenuBarManager))]
[SubsystemHost(typeof(IFileMenuItem))]
public sealed partial class FileMenuManager : ISubsystemHost, IMainMenu
{
    public EditorContext Context { get; }

    public ShortcutSystem Shortcuts { get; }

    public bool ExitRequested { get; private set; }

    public FileMenuManager(MenuBarManager manager)
    {
        Context = manager.Context;
        Shortcuts = manager.Context.Shortcuts;
        InitializeSubsystems();
    }

    public void RequestExit() => ExitRequested = true;

    public void Draw()
    {
        IEnumerable<IFileMenuItem> items = Subsystems;
        ImGuiEx.Menu("File", () =>
        {
            foreach (IFileMenuItem item in items)
            {
                item.Draw();
            }
        });
    }

    /// <summary>Forwards to every item's own overlay, mirroring <see cref="MenuBarManager.DrawOverlay"/>.</summary>
    public void DrawOverlay()
    {
        foreach (IFileMenuItem item in Subsystems)
        {
            item.DrawOverlay();
        }
    }
}
