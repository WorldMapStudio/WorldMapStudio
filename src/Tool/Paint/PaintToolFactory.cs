namespace WorldMapStudio;

/// <summary>Registers the landscape paint tool with the tool window.</summary>
[Subsystem(nameof(ToolWindow))]
public sealed class PaintToolFactory : IToolFactory
{
    private readonly EditorContext _context;

    public float Priority => 1.0f;

    public string Name => "Paint";

    public PaintToolFactory(ToolWindow window)
    {
        _context = window.Context;
    }

    public bool CanActivate() => _context.Landscape.IsEnabled;

    public ITool Create(ToolContext context) => new PaintTool(context);
}
