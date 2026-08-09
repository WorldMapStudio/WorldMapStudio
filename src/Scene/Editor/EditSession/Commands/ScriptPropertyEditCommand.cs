using System.Collections.Generic;
using System.Reflection;

namespace WorldMapStudio;

/// <summary>
/// A generic, reflection-backed edit of one [ScriptProperty(Mutable = true)] — one command per
/// <see cref="ScriptEntityHandle.Set"/> call, the same "one interaction, one command" granularity
/// Phase 1 established for gizmo drags and modal transforms (see ScriptingPlan.md decision #4). Covers
/// every scriptable property on every entity type, built-in or plugin, without a hand-written command
/// per property the way <see cref="TransformEntitiesCommand"/> is hand-written for the gizmo.
/// </summary>
public sealed class ScriptPropertyEditCommand : IEditCommand
{
    private readonly Entity _entity;
    private readonly PropertyInfo _property;
    private readonly object? _before;
    private readonly object? _after;

    public ScriptPropertyEditCommand(Entity entity, PropertyInfo property, object? before, object? after)
    {
        _entity = entity;
        _property = property;
        _before = before;
        _after = after;
        Targets = new IEntity[] { entity };
    }

    public IReadOnlyList<IEntity> Targets { get; }

    public string Description => $"Set {_property.Name} on {_entity.DisplayName}";

    public void Apply() => _property.SetValue(_entity, _after);

    public void Revert() => _property.SetValue(_entity, _before);
}
