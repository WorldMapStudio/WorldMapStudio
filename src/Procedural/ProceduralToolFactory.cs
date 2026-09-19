namespace WorldMapStudio;

[Subsystem(nameof(ToolSystem))]
public sealed class ProceduralToolFactory : IToolFactory
{
    public float Priority => 20f;

    public string Name => "Procedural Mesh";

    public ProceduralToolFactory(ToolSystem tools)
    {
    }

    public ITool Create(ToolContext context) =>
        new NetworkEditTool(context, "Procedural Mesh", entity =>
            entity.Component<ProceduralComponent>() is { ModelId: not null } component ? component : null);
}
