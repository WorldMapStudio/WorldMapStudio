using System.Collections.Generic;
using System.Linq;
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
        (_entities, _before, _after) = ExpandToLoadedDescendants(entities, before, after);
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

    private static (SceneEntity[] Entities, Transform3D[] Before, Transform3D[] After) ExpandToLoadedDescendants(
        SceneEntity[] entities,
        Transform3D[] before,
        Transform3D[] after)
    {
        if (entities.Length != before.Length || entities.Length != after.Length)
        {
            return (entities, before, after);
        }

        var records = new List<TransformRecord>(entities.Length);
        var included = new HashSet<SceneEntity>();
        for (int i = 0; i < entities.Length; i++)
        {
            if (included.Add(entities[i]))
            {
                records.Add(new TransformRecord(entities[i], before[i], after[i]));
            }
        }

        int cursor = 0;
        while (cursor < records.Count)
        {
            TransformRecord parent = records[cursor++];
            Transform3D delta = parent.After * parent.Before.AffineInverse();
            foreach (SceneEntity child in parent.Entity.Children)
            {
                if (!included.Add(child))
                {
                    continue;
                }

                Transform3D childAfter = child.Transform;
                Transform3D childBefore = delta.AffineInverse() * childAfter;
                records.Add(new TransformRecord(child, childBefore, childAfter));
            }
        }

        return (
            records.Select(record => record.Entity).ToArray(),
            records.Select(record => record.Before).ToArray(),
            records.Select(record => record.After).ToArray());
    }

    private readonly record struct TransformRecord(SceneEntity Entity, Transform3D Before, Transform3D After);
}
