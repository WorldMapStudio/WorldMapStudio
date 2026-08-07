using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>Moves or rotates a set of scene entities from one set of world transforms to another.</summary>
public sealed class TransformEntitiesCommand : IEditCommand
{
    private readonly SceneEntity[] _entities;
    private readonly Transform3D[] _before;
    private readonly Transform3D[] _after;

    public TransformEntitiesCommand(SceneEntity[] entities, Transform3D[] before, Transform3D[] after)
    {
        _entities = entities;
        _before = before;
        _after = after;
    }

    public IReadOnlyList<IEntity> Targets => _entities;

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
