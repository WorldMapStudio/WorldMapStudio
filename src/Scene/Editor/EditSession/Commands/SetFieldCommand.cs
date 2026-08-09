using System;
using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// A single field going from one value to another on one entity. The generic counterpart of
/// <see cref="TransformEntitiesCommand"/>, for the many small properties a catalog editor exposes
/// where writing a command class per field would be noise.
///
/// Recorded already-applied, like every command here: the edit ran live in the UI and this captures
/// how to put it back.
/// </summary>
public sealed class SetFieldCommand<T>(Entity entity, string field, Action<T> set, T before, T after) : IEditCommand
{
    public IReadOnlyList<IEntity> Targets { get; } = new IEntity[] { entity };

    public string Description => $"Set {field} on {entity.DisplayName}";

    public void Apply() => set(after);

    public void Revert() => set(before);
}

/// <summary>
/// Brackets an ImGui field so a whole interaction — a drag, or a text edit up to losing focus —
/// becomes one undo step. Capture on activation, record on deactivation, drop no-op edits.
/// </summary>
public sealed class FieldEditTracker
{
    private object? _before;

    /// <summary>
    /// Call immediately after drawing a widget. <paramref name="current"/> is read after the widget
    /// ran, which on the activation frame is still the value before any edit.
    /// </summary>
    public void Track<T>(EditSessionManager sessions, Entity entity, string field, T current, Action<T> set)
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
            sessions.Record(new SetFieldCommand<T>(entity, field, set, before, current));
        }
    }
}
