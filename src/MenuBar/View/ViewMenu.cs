namespace WorldMapStudio;

/// <summary>
/// The "View" menu: hosts <see cref="IMenuItem"/>s for viewport display toggles, streaming settings and
/// per-type show/hide categories. Self-registers with <see cref="MenuBarManager"/>, sitting between
/// Edit and Scene.
/// </summary>
[Subsystem(nameof(MenuBarManager))]
[SubsystemHost(typeof(IMenuItem))]
public sealed partial class ViewMenu : ISubsystemHost, IMainMenu
{
    public EditorContext Context { get; }

    public float Priority => 0.6f;

    public ViewMenu(MenuBarManager manager)
    {
        Context = manager.Context;
        InitializeSubsystems();
    }

    public void Draw() => ImGuiEx.Menu("View", () => MenuItems.Draw(Subsystems));

    public void DrawOverlay() => MenuItems.DrawOverlay(Subsystems);
}
