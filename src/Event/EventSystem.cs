#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Hosts every <see cref="IEventHandler{TEvent}"/> in the editor. Handlers self-register with
/// [Subsystem(nameof(EventSystem))] and are constructed by the generated InitializeSubsystems(),
/// exactly like a <see cref="Storage"/> under <see cref="DatabaseSystem"/> — a plugin adds a new
/// event, or reacts to an existing one, without this class ever naming it.
///
/// Constructed once by <see cref="WorldMapStudioApp"/>, before there is a <see cref="Project"/> or an
/// <see cref="EditorContext"/> — <see cref="StartupEvent"/> exists precisely to run in that gap — so
/// it is kept reachable via <see cref="Current"/> rather than threaded through every call site that
/// might one day need to dispatch something. <see cref="WorkQueue"/> and <see cref="ProjectStore"/>
/// already follow the same app-lifetime-static shape.
/// </summary>
public sealed partial class EventSystem : ISubsystemHost
{
    public static EventSystem Current { get; private set; } = null!;

    public EventSystem()
    {
        Current = this;
        InitializeSubsystems();
    }

    /// <summary>Runs every registered handler of <typeparamref name="TEvent"/>, in ascending
    /// <see cref="ISubsystem.Priority"/> order, stopping as soon as a handler marks the event
    /// <see cref="Event.Handled"/>.</summary>
    public void Dispatch<TEvent>(TEvent e) where TEvent : Event => Dispatch(e, Subsystems);

    /// <summary>Dispatch against an explicit handler list, so tests can exercise ordering and
    /// short-circuiting without constructing a real <see cref="EventSystem"/>.</summary>
    internal static void Dispatch<TEvent>(TEvent e, IEnumerable<ISubsystem> handlers) where TEvent : Event
    {
        foreach (IEventHandler<TEvent> handler in handlers.OfType<IEventHandler<TEvent>>())
        {
            if (e.Handled)
            {
                return;
            }

            handler.Handle(e);
        }
    }
}
