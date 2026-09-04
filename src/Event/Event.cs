#nullable enable
namespace WorldMapStudio;

/// <summary>
/// Base type for everything dispatched through <see cref="EventSystem"/>. A handler that fully
/// resolves the event marks it consumed — via the event's own intent method (e.g.
/// <see cref="StartupEvent.OpenScene"/>), never <see cref="Consume"/> directly — which stops
/// dispatch before the next handler runs.
/// </summary>
public abstract class Event
{
    public bool Handled { get; private set; }

    /// <summary>Called by an event's own intent methods as a side effect of resolving it. Kept
    /// protected so a handler can't mark an event handled without actually doing anything.</summary>
    protected void Consume() => Handled = true;
}
