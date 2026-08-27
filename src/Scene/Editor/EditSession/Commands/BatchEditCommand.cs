using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>Groups several already-applied edits into one undo history entry.</summary>
public sealed class BatchEditCommand : IEditCommand
{
    private readonly IEditCommand[] _commands;

    public BatchEditCommand(string description, IReadOnlyList<IEditCommand> commands)
    {
        Description = description;
        _commands = commands.ToArray();
        Targets = _commands.SelectMany(command => command.Targets).Distinct().ToArray();
    }

    public IReadOnlyList<IEntity> Targets { get; }

    public string Description { get; }

    public void Apply()
    {
        foreach (IEditCommand command in _commands)
        {
            command.Apply();
        }
    }

    public void Revert()
    {
        for (int i = _commands.Length - 1; i >= 0; i--)
        {
            _commands[i].Revert();
        }
    }
}
