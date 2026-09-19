using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The "Map" menu: shows which map is open above its hosted <see cref="IMenuItem"/>s. Self-registers
/// with <see cref="MenuBarManager"/>, between Scene and Window.
/// </summary>
[Subsystem(nameof(MenuBarManager))]
[SubsystemHost(typeof(IMenuItem))]
public sealed partial class MapMenu : ISubsystemHost, IMainMenu
{
    public EditorContext Context { get; }

    public float Priority => 0.85f;

    public MapMenu(MenuBarManager manager)
    {
        Context = manager.Context;
        InitializeSubsystems();
    }

    public void Draw() => ImGuiEx.Menu("Map", () =>
    {
        ImGui.MenuItem(Context.Maps.Current.DisplayName, string.Empty, false, false);
        ImGui.Separator();
        MenuItems.Draw(Subsystems);
    });

    public void DrawOverlay() => MenuItems.DrawOverlay(Subsystems);
}
