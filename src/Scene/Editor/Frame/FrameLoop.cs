using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Runs every <see cref="IFrameParticipant"/> once per frame, in ascending
/// <see cref="IFrameParticipant.TickPriority"/>. Participants are found with the same
/// <see cref="SubsystemTree"/> walk <see cref="WorldLifecycle"/> uses. Main thread only.
/// </summary>
public sealed class FrameLoop
{
    private readonly EditorContext _context;
    private readonly HashSet<IFrameParticipant> _failed = new(ReferenceEqualityComparer.Instance);
    private IFrameParticipant[]? _ordered;
    private int _generation;

    public FrameLoop(EditorContext context)
    {
        _context = context;
    }

    /// <summary>Drops the cached participant list, for a participant added to the tree after the first tick.</summary>
    public void Invalidate() => _ordered = null;

    public void Tick()
    {
        if (_ordered is null || _generation != _context.Lifecycle.Generation)
        {
            _generation = _context.Lifecycle.Generation;
            _ordered = Order(SubsystemTree.Walk(_context).OfType<IFrameParticipant>());
        }

        Run(_ordered, _failed);
    }

    internal static IFrameParticipant[] Order(IEnumerable<IFrameParticipant> participants) =>
        participants.OrderBy(participant => participant.TickPriority).ToArray();

    // Each participant runs inside its own try/catch: one broken participant must not stop the frame.
    // A failure is reported once per participant, not every frame.
    internal static void Run(IFrameParticipant[] ordered, HashSet<IFrameParticipant> failed)
    {
        foreach (IFrameParticipant participant in ordered)
        {
            try
            {
                participant.Update();
            }
            catch (Exception e)
            {
                if (failed.Add(participant))
                {
                    GD.PushError($"[Frame] {participant.GetType().Name} failed to update: {e}");
                }
            }
        }
    }
}
