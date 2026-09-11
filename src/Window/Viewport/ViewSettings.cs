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

    /// <summary>Draws each loaded <see cref="IEnvironmentVolume"/> source's inner/outer spheres.</summary>
    public bool ShowEnvironmentVolumes { get; set; } = true;

    /// <summary>
    /// How many chunks out from the focus <see cref="StreamingSystem"/> keeps entities loaded and
    /// visible, in each horizontal direction. Converted to world units against the open map's
    /// <see cref="LandscapeSettings.ChunkWorldSize"/>, so the same setting covers a fixed amount of
    /// terrain regardless of a map's chunk size.
    /// </summary>
    public int ViewDistanceChunks { get; set; } = 3;

    /// <summary>
    /// How many chunks along each edge share one mesh, material and alpha texture. Purely how terrain is
    /// grouped for rendering: a batch is not an authoring unit, nothing is stored per batch, and the
    /// chunks inside one are built exactly as they were. 1 gives every chunk its own mesh and material,
    /// which is what the editor did before batching existed.
    /// </summary>
    public int TerrainBatchChunks { get; set; } = 4;
}
