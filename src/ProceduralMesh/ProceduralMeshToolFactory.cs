namespace WorldMapStudio;

[Subsystem(nameof(ToolWindow))]
public sealed class ProceduralMeshToolFactory : IToolFactory
{
    public float Priority => 20f;

    public string Name => "Procedural Mesh";

    public ProceduralMeshToolFactory(ToolWindow window)
    {
    }

    public ITool Create(ToolContext context) =>
        new NetworkEditTool(context, "Procedural Mesh", entity =>
            entity.Component<ProceduralMeshComponent>() is { ModelId: not null } component ? component : null);
}
