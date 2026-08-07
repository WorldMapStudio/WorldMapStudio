using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Hosts the top-level entries of the editor's main menu bar (File, Window, ...) as
/// <see cref="ISubsystem"/>s. Entries declare [Subsystem(nameof(MenuBarManager))] and are
/// constructed automatically by the generated InitializeSubsystems(), so a new top-level menu
/// (and everything it hosts) can register itself without touching <see cref="Editor"/>.
/// </summary>
[Subsystem(nameof(EditorContext))]
public sealed partial class MenuBarManager : ISubsystemHost, ISubsystem
{
    /// <summary>The editor's shared systems and project, forwarded down to the menus and windows.</summary>
    public EditorContext Context { get; }

    public float Priority => 0f;

    public MenuBarManager(EditorContext context)
    {
        Context = context;
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
