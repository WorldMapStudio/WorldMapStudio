using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>Covers <see cref="FrameLoop"/>'s ordering and failure isolation against fake participants.</summary>
public static class FrameLoopTests
{
    private sealed class FakeParticipant(List<string> log, string name, float priority, bool throws = false) : IFrameParticipant
    {
        public float TickPriority => priority;

        public void Update()
        {
            log.Add(name);
            if (throws)
            {
                throw new InvalidOperationException($"{name} broke");
            }
        }
    }

    [EditorTest(Category = "FrameLoop", Thread = TestThread.Background)]
    public static void Participants_tick_in_ascending_priority_with_ties_in_discovery_order()
    {
        var log = new List<string>();
        IFrameParticipant[] ordered = FrameLoop.Order(
        [
            new FakeParticipant(log, "late", 5f),
            new FakeParticipant(log, "tieFirst", 0f),
            new FakeParticipant(log, "early", -1f),
            new FakeParticipant(log, "tieSecond", 0f),
        ]);

        FrameLoop.Run(ordered, []);

        Assert.AreEqual("early,tieFirst,tieSecond,late", string.Join(",", log));
    }

    [EditorTest(Category = "FrameLoop", Thread = TestThread.Background)]
    public static void A_participant_that_throws_does_not_stop_the_frame_and_is_reported_once()
    {
        var log = new List<string>();
        var broken = new FakeParticipant(log, "broken", 1f, throws: true);
        IFrameParticipant[] ordered = FrameLoop.Order(
        [
            new FakeParticipant(log, "first", 0f),
            broken,
            new FakeParticipant(log, "last", 2f),
        ]);
        var failed = new HashSet<IFrameParticipant>(ReferenceEqualityComparer.Instance);

        FrameLoop.Run(ordered, failed);
        FrameLoop.Run(ordered, failed);

        Assert.AreEqual(2, log.Count(entry => entry == "first"));
        Assert.AreEqual(2, log.Count(entry => entry == "last"), "the participants after the broken one still ran every frame");
        Assert.AreEqual(1, failed.Count, "the failure is recorded once, not per frame");
    }
}
