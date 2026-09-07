namespace WorldMapStudio;

/// <summary>
/// One entry in the strip of controls drawn across the top of the <see cref="ViewportWindow"/>,
/// hosted as an <see cref="ISubsystem"/> of <see cref="ViewportHeader"/>. Implementations declare
/// [Subsystem(nameof(ViewportHeader))] to self-register; entries are ordered by
/// <see cref="ISubsystem.Priority"/> and separated automatically.
/// </summary>
public interface IViewportHeaderItem : ISubsystem
{
    /// <summary>Draws this entry inline on the header row. The caller has already placed the cursor
    /// and pushed a unique ID scope, so the item just emits its own widgets.</summary>
    void Draw(in ViewportHeaderContext context);
}
