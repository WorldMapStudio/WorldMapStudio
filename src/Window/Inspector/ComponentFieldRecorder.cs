using System;
using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// Records a one-shot field edit (a combo, checkbox or button, as opposed to a drag) as an undo
/// command, skipping no-op writes. For drag-style widgets that need begin/end bracketing across a
/// whole interaction, use <see cref="ComponentFieldEditTracker"/> instead.
/// </summary>
public static class ComponentFieldRecorder
{
    public static void Record<T>(InspectorContext context, SceneComponent component, string field, T before, T after, Action<T> set)
    {
        if (EqualityComparer<T>.Default.Equals(before, after))
        {
            return;
        }

        var command = new SetComponentFieldCommand<T>(component, field, set, before, after);
        command.Apply();
        context.Sessions.Record(command);
    }
}
