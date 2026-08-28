namespace WorldMapStudio;

/// <summary>
/// Editor-wide viewport display toggles, shared between the "View" menu and the
/// <see cref="ViewportWindow"/> that renders them.
/// </summary>
public sealed class ViewSettings
{
    public bool ShowGrid { get; set; } = true;

    /// <summary>
    /// Draws the landscape's chunk boundaries onto the terrain itself. On the surface rather than on
    /// the ground grid, so the lines drape over deformed ground instead of sinking into it — and so
    /// they never fight the terrain for the same depth.
    /// </summary>
    public bool ShowChunkEdges { get; set; } = true;

    /// <summary>Whether the viewport applies the blended <see cref="EnvironmentValues"/> (sky, sun,
    /// fog) or stays at the flat grey look the editor always used before <see cref="EnvironmentSystem"/>
    /// existed. On by default; turning it off is a one-click fallback to that flat look for
    /// terrain/asset authoring where an accurate scene light is a distraction.</summary>
    public bool UseEnvironmentLighting { get; set; } = true;

    /// <summary>Draws each loaded <see cref="IEnvironmentVolume"/> source's inner/outer spheres.</summary>
    public bool ShowEnvironmentVolumes { get; set; } = true;
}
