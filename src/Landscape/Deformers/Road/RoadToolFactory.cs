namespace WorldMapStudio;

[Subsystem(nameof(ToolWindow))]
public sealed class RoadToolFactory : IToolFactory
{
    public float Priority => 21f;

    public string Name => "Road";

    public RoadToolFactory(ToolWindow window)
    {
    }

    public ITool Create(ToolContext context) =>
        new NetworkEditTool(context, "Road", entity => entity.Component<RoadComponent>());
}
