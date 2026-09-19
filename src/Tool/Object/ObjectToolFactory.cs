namespace WorldMapStudio;

/// <summary>Registers the default object-mode tool with the tool system.</summary>
[Subsystem(nameof(ToolSystem))]
public sealed class ObjectToolFactory : IToolFactory
{
    public string Name => "Object";

    public ObjectToolFactory(ToolSystem tools)
    {
    }

    public ITool Create(ToolContext context) => new ObjectTool(context);
}
