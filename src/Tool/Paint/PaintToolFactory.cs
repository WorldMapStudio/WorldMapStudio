namespace WorldMapStudio;

/// <summary>Registers the landscape paint tool with the tool system.</summary>
[Subsystem(nameof(ToolSystem))]
public sealed class PaintToolFactory : IToolFactory
{
    private readonly EditorContext _context;

    public float Priority => 1.0f;

    public string Name => "Paint";

    public PaintToolFactory(ToolSystem tools)
    {
        _context = tools.Context.Editor;
    }

    public bool CanActivate() => _context.Landscape.IsEnabled;

    public ITool Create(ToolContext context) => new PaintTool(context);
}
