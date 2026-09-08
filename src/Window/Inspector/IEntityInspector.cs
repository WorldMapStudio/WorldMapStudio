using System;
using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// Draws and edits one or more selected entities of a given type. Inspectors self-register with
/// <see cref="InspectorWindow"/>, which picks the one whose <see cref="TargetType"/> is the closest
/// common ancestor of everything selected.
/// </summary>
public interface IEntityInspector : ISubsystem
{
    Type TargetType { get; }

    /// <summary>Whether the inspector window shows its "filter fields" box for this inspector. Off for
    /// an inspector with nothing to filter, such as a read-only all-derived view.</summary>
    bool ShowFieldFilter => true;

    void Draw(InspectorContext context, IReadOnlyList<IEntity> targets);
}
