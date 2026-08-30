namespace WorldMapStudio;

/// <summary>
/// A single entry in the "File" menu, hosted as an <see cref="ISubsystem"/> of
/// <see cref="FileMenuManager"/>. Implementations declare [Subsystem(nameof(FileMenuManager))]
/// to self-register.
/// </summary>
public interface IFileMenuItem : ISubsystem
{
    public void Draw();

    /// <summary>
    /// Drawn once per frame at the root ImGui level, mirroring <see cref="IMainMenu.DrawOverlay"/>: an
    /// item whose action needs a popup (a confirm dialog, say) draws it here rather than from
    /// <see cref="Draw"/>, which runs inside the File menu's own ID stack.
    /// </summary>
    public void DrawOverlay() { }
}
