namespace WorldMapStudio;

/// <summary>
/// Reacts to one kind of <see cref="Event"/>. Self-registers with [Subsystem(nameof(EventSystem))]
/// like any other subsystem; a class may implement this more than once (for different event types)
/// to handle several kinds of event. Handlers of the same event type run in ascending
/// <see cref="ISubsystem.Priority"/> order, and dispatch stops as soon as one marks the event
/// <see cref="Event.Handled"/>.
/// </summary>
public interface IEventHandler<in TEvent> : ISubsystem where TEvent : Event
{
    void Handle(TEvent e);
}
