namespace WorldMapStudio;

/// <summary>
/// A single entry in the "Operations" menu, hosted as an <see cref="ISubsystem"/> of
/// <see cref="OperationsMenu"/>. Implementations declare [Subsystem(nameof(OperationsMenu))] to
/// self-register.
/// </summary>
public interface IOperation : ISubsystem
{
    public string Name { get; }

    /// <summary>Bound keyboard shortcut, formatted for display, or empty if none is bound.</summary>
    public string ShortcutLabel => string.Empty;

    public bool CanApply();

    public void Apply();
}
