using System;
using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// A generic edit of one component field or array element, recorded from script — the component-level
/// sibling of <see cref="ScriptPropertyEditCommand"/>. Takes pre-bound apply/revert delegates rather
/// than a <see cref="System.Reflection.PropertyInfo"/> directly so the same class covers both a
/// whole-property write (<see cref="SceneScriptApi.SetComponentField"/>) and an array-element write
/// (<see cref="SceneScriptApi.SetComponentArrayField"/>, needed for fields like
/// <c>WowLightComponent.ParamIds</c> that have no property setter of their own) without a compile-time
/// generic type parameter, the way <see cref="SetComponentFieldCommand{T}"/> needs one.
/// </summary>
public sealed class ScriptComponentFieldEditCommand : IEditCommand, IChunkChangeCommand
{
    private readonly SceneComponent _component;
    private readonly SceneEntity _entity;
    private readonly string _field;
    private readonly Action _apply;
    private readonly Action _revert;

    public ScriptComponentFieldEditCommand(SceneComponent component, string field, Action apply, Action revert)
    {
        _component = component;
        _entity = component.Owner ?? throw new InvalidOperationException("Component is not attached.");
        _field = field;
        _apply = apply;
        _revert = revert;
        Targets = new IEntity[] { _entity };
        ChunkImpacts = CaptureImpacts();
    }

    public IReadOnlyList<IEntity> Targets { get; }

    public IReadOnlyList<ChunkChangeImpact> ChunkImpacts { get; }

    public string Description => $"Set {_field} on {_component.DisplayName}";

    public void Apply() => _apply();

    public void Revert() => _revert();

    private IReadOnlyList<ChunkChangeImpact> CaptureImpacts()
    {
        _revert();
        ChunkChangeSnapshot beforeSnapshot = ChunkChangeSnapshot.Capture(_entity);
        _apply();
        ChunkChangeSnapshot afterSnapshot = ChunkChangeSnapshot.Capture(_entity);
        return [new ChunkChangeImpact(_entity, beforeSnapshot, afterSnapshot)];
    }
}
