namespace WorldMapStudio;

/// <summary>
/// One control on the <see cref="ViewportWindow"/>'s toolbar row, drawn inline after the active
/// tool's own controls. Hosted as an <see cref="ISubsystem"/> of <see cref="ViewportHeader"/>;
/// implementations declare [Subsystem(nameof(ViewportHeader))] to self-register. Entries are ordered
/// by <see cref="ISubsystem.Priority"/> and separated automatically.
/// </summary>
public interface IViewportHeaderItem : ISubsystem
{
    /// <summary>Draws this entry inline on the header row. The caller has already placed the cursor
    /// and pushed a unique ID scope, so the item just emits its own widgets.</summary>
    void Draw(in ViewportHeaderContext context);
}
