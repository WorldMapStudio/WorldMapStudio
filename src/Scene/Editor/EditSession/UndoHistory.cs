using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// A linear undo/redo stack of <see cref="IEditCommand"/>s. Commands are recorded already-applied;
/// <see cref="Undo"/> reverts the newest and <see cref="Redo"/> re-applies it. Recording a new
/// command clears the redo stack.
/// </summary>
public sealed class UndoHistory
{
    private readonly List<IEditCommand> _undo = [];
    private readonly List<IEditCommand> _redo = [];

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Record(IEditCommand command)
    {
        _undo.Add(command);
        _redo.Clear();
    }

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        IEditCommand command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Revert();
        _redo.Add(command);
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        IEditCommand command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        command.Apply();
        _undo.Add(command);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
