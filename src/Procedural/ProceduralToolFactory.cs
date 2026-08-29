namespace WorldMapStudio;

[Subsystem(nameof(ToolWindow))]
public sealed class ProceduralToolFactory : IToolFactory
{
    public float Priority => 20f;

    public string Name => "Procedural Mesh";

    public ProceduralToolFactory(ToolWindow window)
    {
    }

    public ITool Create(ToolContext context) =>
        new NetworkEditTool(context, "Procedural Mesh", entity =>
            entity.Component<ProceduralComponent>() is { ModelId: not null } component ? component : null);
}
