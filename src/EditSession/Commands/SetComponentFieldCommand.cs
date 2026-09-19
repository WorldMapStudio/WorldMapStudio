using System;
using System.Collections.Generic;

namespace WorldMapStudio;

public sealed class SetComponentFieldCommand<T> : IEditCommand, IChunkChangeCommand
{
    private readonly SceneEntity _entity;
    private readonly SceneComponent _component;
    private readonly string _field;
    private readonly Action<T> _set;
    private readonly T _before;
    private readonly T _after;

    public SetComponentFieldCommand(SceneComponent component, string field, Action<T> set, T before, T after)
    {
        _component = component;
        _entity = component.Owner ?? throw new InvalidOperationException("Component is not attached.");
        _field = field;
        _set = set;
        _before = before;
        _after = after;
        Targets = new IEntity[] { _entity };
        ChunkImpacts = CaptureImpacts();
    }

    public IReadOnlyList<IEntity> Targets { get; }

    public IReadOnlyList<ChunkChangeImpact> ChunkImpacts { get; }

    public string Description => $"Set {_field} on {_component.DisplayName}";

    public void Apply() => _set(_after);

    public void Revert() => _set(_before);

    private IReadOnlyList<ChunkChangeImpact> CaptureImpacts()
    {
        _set(_before);
        ChunkChangeSnapshot beforeSnapshot = ChunkChangeSnapshot.Capture(_entity);
        _set(_after);
        ChunkChangeSnapshot afterSnapshot = ChunkChangeSnapshot.Capture(_entity);
        return [new ChunkChangeImpact(_entity, beforeSnapshot, afterSnapshot)];
    }
}

public sealed class ComponentFieldEditTracker
{
    private object? _before;

    public void Track<T>(EditSessionManager sessions, SceneComponent component, string field, T current, Action<T> set)
    {
        if (ImGuiNET.ImGui.IsItemActivated())
        {
            _before = current;
        }

        if (!ImGuiNET.ImGui.IsItemDeactivatedAfterEdit() || _before is not T before)
        {
            return;
        }

        _before = null;
        if (!EqualityComparer<T>.Default.Equals(before, current))
        {
            sessions.Record(new SetComponentFieldCommand<T>(component, field, set, before, current));
        }
    }
}
