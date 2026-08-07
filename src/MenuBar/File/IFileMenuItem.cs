namespace WorldMapStudio;

/// <summary>
/// A single entry in the "File" menu, hosted as an <see cref="ISubsystem"/> of
/// <see cref="FileMenuManager"/>. Implementations declare [Subsystem(nameof(FileMenuManager))]
/// to self-register.
/// </summary>
public interface IFileMenuItem : ISubsystem
{
    public void Draw();
}
