using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>Groups several already-applied edits into one undo history entry. Aggregates the chunk
/// impacts of any wrapped command that reports them, so a batch of chunk-affecting edits (e.g. pasting
/// several entities) still dirties the right chunks on commit.</summary>
public sealed class BatchEditCommand : IEditCommand, IChunkChangeCommand
{
    private readonly IEditCommand[] _commands;

    public BatchEditCommand(string description, IReadOnlyList<IEditCommand> commands)
    {
        Description = description;
        _commands = commands.ToArray();
        Targets = _commands.SelectMany(command => command.Targets).Distinct().ToArray();
        ChunkImpacts = _commands.OfType<IChunkChangeCommand>().SelectMany(command => command.ChunkImpacts).ToArray();
    }

    public IReadOnlyList<IEntity> Targets { get; }

    public IReadOnlyList<ChunkChangeImpact> ChunkImpacts { get; }

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
