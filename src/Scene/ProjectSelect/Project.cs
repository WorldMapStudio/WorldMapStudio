namespace WorldMapStudio;

/// <summary>
/// A named project selected into the editor. Serialization (databases, settings, disk layout) is not
/// modelled yet, so for now a project is just a display name held in memory by <see cref="ProjectSelect"/>.
/// </summary>
public sealed class Project
{
    public required string Name { get; set; }
}
