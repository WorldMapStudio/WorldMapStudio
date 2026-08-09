using System;
using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// Tracks the entities edited through it (pinning them in memory) and owns their undo history until
/// the session is committed or aborted. Committing keeps the edits and clears the history; aborting
/// reverts every edit back to the session's starting state.
/// </summary>
public sealed class EditSession
{
    private readonly HashSet<IEntity> _pinned = [];

    public UndoHistory History { get; } = new();

    /// <summary>Entities held in memory for the lifetime of the session.</summary>
    public IReadOnlyCollection<IEntity> Pinned => _pinned;

    public bool IsDirty => _pinned.Count > 0;

    /// <summary>
    /// Records an already-applied command into the history and pins its targets.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A target is an <see cref="IDerivedEntity"/>. Derived entities are computed from other
    /// entities, so editing one directly has nothing to save and no meaning — the fix is to edit
    /// whatever produced it. Loud on purpose: silently dropping the pin would leave a command in the
    /// history that undoes into a value the next rebuild discards.
    /// </exception>
    public void Record(IEditCommand command)
    {
        foreach (IEntity target in command.Targets)
        {
            if (target is IDerivedEntity)
            {
                throw new InvalidOperationException(
                    $"'{command.Description}' targets derived entity {target.Id}, which cannot be edited directly.");
            }
        }

        foreach (IEntity target in command.Targets)
        {
            _pinned.Add(target);
        }

        History.Record(command);
    }

    /// <summary>Keeps all edits; clears the undo history and unpins. Persistence hooks in here later.</summary>
    public void Commit()
    {
        History.Clear();
        _pinned.Clear();
    }

    /// <summary>Reverts every recorded edit, newest first, then clears the session.</summary>
    public void Abort()
    {
        while (History.CanUndo)
        {
            History.Undo();
        }

        History.Clear();
        _pinned.Clear();
    }
}
