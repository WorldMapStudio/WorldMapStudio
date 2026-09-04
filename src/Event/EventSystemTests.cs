#nullable enable
using System;
using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// Covers dispatch order and short-circuiting via <see cref="EventSystem.Dispatch{TEvent}(TEvent,
/// IEnumerable{ISubsystem})"/> directly, since the handlers here aren't real self-registered
/// subsystems.
/// </summary>
public static class EventSystemTests
{
    private sealed class RecordingEvent : Event
    {
        public List<string> Log { get; } = [];
        public void MarkHandled() => Consume();
    }

    private sealed class OtherEvent : Event;

    private sealed class RecordingHandler(string name, float priority, bool consumes = false) : IEventHandler<RecordingEvent>
    {
        public float Priority => priority;

        public void Handle(RecordingEvent e)
        {
            e.Log.Add(name);
            if (consumes)
            {
                e.MarkHandled();
            }
        }
    }

    private sealed class MultiEventHandler : IEventHandler<RecordingEvent>, IEventHandler<OtherEvent>
    {
        public float Priority => 0f;
        public int RecordingHandled { get; private set; }
        public int OtherHandled { get; private set; }

        public void Handle(RecordingEvent e) => RecordingHandled++;
        public void Handle(OtherEvent e) => OtherHandled++;
    }

    [EditorTest(Category = "Event")]
    public static void Handlers_run_in_ascending_priority_order()
    {
        var e = new RecordingEvent();
        EventSystem.Dispatch(e, new ISubsystem[]
        {
            new RecordingHandler("second", 1f),
            new RecordingHandler("first", 0f),
        });

        Assert.AreEqual(2, e.Log.Count);
        Assert.AreEqual("first", e.Log[0]);
        Assert.AreEqual("second", e.Log[1]);
    }

    [EditorTest(Category = "Event")]
    public static void A_handler_that_consumes_the_event_stops_the_rest()
    {
        var e = new RecordingEvent();
        EventSystem.Dispatch(e, new ISubsystem[]
        {
            new RecordingHandler("first", 0f, consumes: true),
            new RecordingHandler("second", 1f),
        });

        Assert.AreEqual(1, e.Log.Count);
        Assert.AreEqual("first", e.Log[0]);
        Assert.IsTrue(e.Handled);
    }

    [EditorTest(Category = "Event")]
    public static void A_class_can_handle_more_than_one_event_type()
    {
        var multi = new MultiEventHandler();

        EventSystem.Dispatch(new RecordingEvent(), new ISubsystem[] { multi });
        EventSystem.Dispatch(new OtherEvent(), new ISubsystem[] { multi });

        Assert.AreEqual(1, multi.RecordingHandled);
        Assert.AreEqual(1, multi.OtherHandled);
    }

    [EditorTest(Category = "Event")]
    public static void An_event_with_no_handlers_is_left_unhandled()
    {
        var e = new RecordingEvent();
        EventSystem.Dispatch(e, Array.Empty<ISubsystem>());

        Assert.IsFalse(e.Handled);
    }
}
