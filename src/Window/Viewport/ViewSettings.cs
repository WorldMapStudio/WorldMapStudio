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
}
