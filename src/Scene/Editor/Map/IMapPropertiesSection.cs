namespace WorldMapStudio;

/// <summary>
/// One self-registered block of per-map settings, drawn in the map picker's properties area for a
/// given <see cref="Map"/> — the <see cref="Map"/> analogue of <see cref="ISceneComponentType"/>: the
/// picker knows nothing about what any section actually configures, so a plugin (WoW lighting's default
/// light, say) adds a concept to "map properties" without this core type ever naming it.
/// </summary>
public interface IMapPropertiesSection : ISubsystem
{
    /// <summary>Heading shown above this section, e.g. "Default Light".</summary>
    string Label { get; }

    /// <summary>
    /// Draws this section's fields for <paramref name="map"/>. <paramref name="isCurrent"/> is whether
    /// <paramref name="map"/> is the currently open map — most sections will want to disable editing (or
    /// say so) otherwise, since only the current map's entities are actually loaded to edit; see
    /// <see cref="EditorContext.Scene"/>/<see cref="MapSystem.CurrentMap"/>.
    /// </summary>
    void Draw(EditorContext context, Map map, bool isCurrent);
}
