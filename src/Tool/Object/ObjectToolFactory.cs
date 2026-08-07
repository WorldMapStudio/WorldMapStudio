namespace WorldMapStudio;

/// <summary>Registers the default object-mode tool with the tool window.</summary>
[Subsystem(nameof(ToolWindow))]
public sealed class ObjectToolFactory : IToolFactory
{
    public float Priority => 0f;

    public string Name => "Object";

    public ObjectToolFactory(ToolWindow window)
    {
    }

    public ITool Create(ToolContext context) => new ObjectTool(context);
}
