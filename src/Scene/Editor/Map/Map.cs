namespace WorldMapStudio;

/// <summary>
/// A map the editor can open: an id that scene entities are scoped to, plus a human name. Maps come
/// from <see cref="IMapSource"/>s, so the same model covers a map the editor created and one a plugin
/// read out of a game client's tables.
/// </summary>
public sealed class Map(MapId id, string name)
{
    /// <summary>What <see cref="SceneEntity.Map"/> stores; stable and unique across all sources.</summary>
    public MapId Id { get; } = id;

    public string Name { get; set; } = name;

    public string DisplayName => Name.Length > 0 ? Name : $"Map {Id.Value}";
}
