using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// A single reversible edit. Commands are recorded already-applied (the edit ran live in the
/// viewport or inspector), then replayed by <see cref="UndoHistory"/> via <see cref="Revert"/> and
/// <see cref="Apply"/>.
/// </summary>
public interface IEditCommand
{
    /// <summary>Entities this command touches, so the edit session can pin them in memory.</summary>
    IReadOnlyList<IEntity> Targets { get; }

    /// <summary>Short label shown in the undo history window, e.g. "Create Empty 3".</summary>
    string Description { get; }

    void Apply();

    void Revert();
}
