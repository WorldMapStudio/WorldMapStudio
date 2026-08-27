using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>Changes a scene entity's parent, keeping grouping undoable and preventing cycles.</summary>
public sealed class SetSceneEntityParentCommand : IEditCommand
{
    private readonly SceneEntity _entity;
    private readonly SceneEntity? _before;
    private readonly SceneEntity? _after;
    private readonly int? _beforeRecordId;
    private readonly int? _afterRecordId;

    public SetSceneEntityParentCommand(SceneEntity entity, SceneEntity? before, SceneEntity? after)
    {
        if (!CanParentTo(entity, after))
        {
            throw new InvalidOperationException("An entity cannot be parented to itself or one of its descendants.");
        }

        if (after != null && after.Map != entity.Map)
        {
            throw new InvalidOperationException("An entity cannot be parented to an entity in another map.");
        }

        _entity = entity;
        _before = before;
        _after = after;
        _beforeRecordId = before?.RecordId ?? entity.ParentRecordId;
        _afterRecordId = after?.RecordId;
        Targets = new[] { entity }.Concat(Parents(before, after)).Distinct().ToArray();
    }

    public IReadOnlyList<IEntity> Targets { get; }

    public string Description => _after == null
        ? $"Clear parent on {_entity.DisplayName}"
        : $"Parent {_entity.DisplayName} to {_after.DisplayName}";

    public void Apply() => Set(_after, _afterRecordId);

    public void Revert() => Set(_before, _beforeRecordId);

    private void Set(SceneEntity? parent, int? parentRecordId)
    {
        _entity.Parent = parent;
        _entity.ParentRecordId = parent?.RecordId ?? parentRecordId;
    }

    private static IEnumerable<SceneEntity> Parents(params SceneEntity?[] parents)
    {
        foreach (SceneEntity? parent in parents)
        {
            if (parent != null)
            {
                yield return parent;
            }
        }
    }

    public static bool CanParentTo(SceneEntity entity, SceneEntity? parent)
    {
        if (parent == null)
        {
            return true;
        }

        if (ReferenceEquals(entity, parent) || parent.Map != entity.Map)
        {
            return false;
        }

        for (SceneEntity? current = parent; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, entity))
            {
                return false;
            }
        }

        return true;
    }
}
