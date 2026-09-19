using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>Moves or rotates a set of scene entities from one set of world transforms to another.</summary>
public sealed class TransformEntitiesCommand : IEditCommand, IChunkChangeCommand
{
    private readonly SceneEntity[] _entities;
    private readonly Transform3D[] _before;
    private readonly Transform3D[] _after;
    private readonly IReadOnlyList<ChunkChangeImpact> _chunkImpacts;

    public TransformEntitiesCommand(SceneEntity[] entities, Transform3D[] before, Transform3D[] after)
    {
        _entities = entities;
        _before = before;
        _after = after;
        var impacts = new ChunkChangeImpact[_entities.Length];
        for (int i = 0; i < _entities.Length; i++)
        {
            impacts[i] = new ChunkChangeImpact(
                _entities[i],
                ChunkChangeSnapshot.Capture(_entities[i], _before[i]),
                ChunkChangeSnapshot.Capture(_entities[i], _after[i]));
        }

        _chunkImpacts = impacts;
    }

    public IReadOnlyList<IEntity> Targets => _entities;

    public IReadOnlyList<ChunkChangeImpact> ChunkImpacts => _chunkImpacts;

    public string Description => _entities.Length == 1
        ? $"Transform {_entities[0].DisplayName}"
        : $"Transform {_entities.Length} entities";

    public void Apply()
    {
        for (int i = 0; i < _entities.Length; i++)
        {
            _entities[i].Transform = _after[i];
        }
    }

    public void Revert()
    {
        for (int i = 0; i < _entities.Length; i++)
        {
            _entities[i].Transform = _before[i];
        }
    }
}
