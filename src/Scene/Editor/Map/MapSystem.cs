namespace WorldMapStudio;

/// <summary>
/// Tracks which map the editor is currently viewing and editing. Scene streaming loads entities for
/// this map, and newly created entities are placed in it.
/// </summary>
public sealed class MapSystem
{
    public MapId CurrentMap { get; set; } = new(0);
}
