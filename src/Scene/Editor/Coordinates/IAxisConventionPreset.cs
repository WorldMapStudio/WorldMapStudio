namespace WorldMapStudio;

/// <summary>
/// Supplies a named axis convention a project can start from, so a user picks "the game I'm mapping
/// for" instead of hand-assigning three axis directions.
///
/// The editor defines only the Godot-default preset itself — a plugin for a specific game registers
/// its own convention with [Subsystem(nameof(AxisConventionPresets))] and it becomes selectable
/// wherever a project's axis convention is set up.
/// </summary>
public interface IAxisConventionPreset : ISubsystem
{
    /// <summary>Name shown when picking a preset.</summary>
    string Name { get; }

    /// <summary>One line on what this preset targets.</summary>
    string Description { get; }

    /// <summary>A fresh convention for this target. Callers own and may edit the result.</summary>
    AxisConvention CreateConvention();
}
