namespace WorldMapStudio;

/// <summary>The shared systems a tool operates on, handed to it at creation.</summary>
public sealed class ToolContext(
    SelectionSystem selection,
    SceneEntityRegistry scene,
    EditSessionManager sessions,
    AxisConvention axes)
{
    public SelectionSystem Selection { get; } = selection;
    public SceneEntityRegistry Scene { get; } = scene;
    public EditSessionManager Sessions { get; } = sessions;
    public AxisConvention Axes { get; } = axes;
}
