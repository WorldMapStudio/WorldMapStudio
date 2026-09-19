namespace WorldMapStudio;

/// <summary>
/// Hosts the editor's "File" menu entries as <see cref="ISubsystem"/>s. Items declare
/// [Subsystem(nameof(FileMenuManager))] and are constructed automatically by the generated
/// InitializeSubsystems(), letting features like "Exit" register themselves instead of being
/// wired up by hand in <see cref="Editor"/>. FileMenuManager is itself a top-level menu, self-
/// registered with <see cref="MenuBarManager"/>.
/// </summary>
[Subsystem(nameof(MenuBarManager))]
[SubsystemHost(typeof(IMenuItem))]
public sealed partial class FileMenuManager : ISubsystemHost, IMainMenu
{
    public EditorContext Context { get; }

    public ShortcutSystem Shortcuts { get; }

    public FileMenuManager(MenuBarManager manager)
    {
        Context = manager.Context;
        Shortcuts = manager.Context.Shortcuts;
        InitializeSubsystems();
    }

    public void Draw() => ImGuiEx.Menu("File", () => MenuItems.Draw(Subsystems));

    public void DrawOverlay() => MenuItems.DrawOverlay(Subsystems);
}
