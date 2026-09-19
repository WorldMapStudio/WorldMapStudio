namespace WorldMapStudio;

/// <summary>
/// The "Operations" menu: bulk edits that act on the current selection. Self-registers with
/// <see cref="MenuBarManager"/>, sitting between View and Scene, and hosts its <see cref="IMenuItem"/>s.
/// </summary>
[Subsystem(nameof(MenuBarManager))]
[SubsystemHost(typeof(IMenuItem))]
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

    public void Draw() => ImGuiEx.Menu("Operations", () => MenuItems.Draw(Subsystems));

    public void DrawOverlay() => MenuItems.DrawOverlay(Subsystems);
}
