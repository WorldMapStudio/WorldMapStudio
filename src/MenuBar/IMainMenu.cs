namespace WorldMapStudio;

/// <summary>
/// A top-level entry in the main menu bar (e.g. "File", "Window"), hosted as an
/// <see cref="ISubsystem"/> of <see cref="MenuBarManager"/>. Implementations declare
/// [Subsystem(nameof(MenuBarManager))] to self-register.
/// </summary>
public interface IMainMenu : ISubsystem
{
    public void Draw();

    /// <summary>
    /// Drawn once per frame at the root ImGui level, after the menu bar and the windows. Popups a menu
    /// opens must live here: <c>OpenPopup</c> and <c>BeginPopupModal</c> have to sit at the same level
    /// of the ID stack, and a menu's own <see cref="Draw"/> runs inside the main menu bar's.
    /// </summary>
    public void DrawOverlay() { }
}
