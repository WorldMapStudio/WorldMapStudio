namespace WorldMapStudio;

/// <summary>Identifies the map a scene entity lives in (e.g. a game map/zone id).</summary>
public readonly record struct MapId(int Value)
{
    public override string ToString() => $"map:{Value}";
}
