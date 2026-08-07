namespace WorldMapStudio;

/// <summary>
/// A top-level entry in the main menu bar (e.g. "File", "Window"), hosted as an
/// <see cref="ISubsystem"/> of <see cref="MenuBarManager"/>. Implementations declare
/// [Subsystem(nameof(MenuBarManager))] to self-register.
/// </summary>
public interface IMainMenu : ISubsystem
{
    public void Draw();
}
