namespace WorldMapStudio;

/// <summary>
/// A named project selected into the editor. Serialization (databases, disk layout) is not modelled
/// yet, so for now a project is a display name plus its editing settings, held in memory by
/// <see cref="ProjectSelect"/>. The <see cref="AxisConvention"/> is the coordinate system the user
/// works in; the editor routes everything through it when talking to Godot.
/// </summary>
public sealed class Project
{
    public required string Name { get; set; }

    public AxisConvention AxisConvention { get; set; } = AxisConvention.GodotDefault;
}
