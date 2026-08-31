namespace WorldMapStudio;

/// <summary>
/// The built-in starting point: no remapping at all. Not a real game's convention — it exists so the
/// preset picker always has at least one entry before any plugin registers its own.
/// </summary>
[Subsystem(nameof(AxisConventionPresets))]
public sealed class GodotAxisPreset : IAxisConventionPreset
{
    public GodotAxisPreset(AxisConventionPresets presets)
    {
    }

    public float Priority => -100.0f; // first in the list, as the default choice

    public string Name => "Godot (default)";

    public string Description => "No remapping: matches Godot's own axes.";

    public AxisConvention CreateConvention() => AxisConvention.GodotDefault;
}
