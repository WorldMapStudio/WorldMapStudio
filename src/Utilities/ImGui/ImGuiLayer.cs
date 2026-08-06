using Godot;

namespace WorldMapStudio;

internal sealed partial class ImGuiLayer : CanvasLayer
{
    private readonly ImGuiRenderer _renderer;
    private Rid _canvasItem;
    private Transform2D _finalTransform = Transform2D.Identity;
    private Viewport _parentViewport = null!;
    private Vector2I _subViewportSize = Vector2I.Zero;

    public ImGuiLayer(ImGuiRenderer renderer)
    {
        _renderer = renderer;
    }

    public Rid SubViewportRid { get; private set; }

    public override void _EnterTree()
    {
        Name = "ImGuiLayer";
        Layer = 128;

        _parentViewport = GetViewport();
        SubViewportRid = CreateSubViewport(GetWindow().GetViewportRid());
        _canvasItem = RenderingServer.CanvasItemCreate();
        RenderingServer.CanvasItemSetParent(_canvasItem, GetCanvas());
        _renderer.InitViewport(SubViewportRid);
    }

    public override void _ExitTree()
    {
        RenderingServer.FreeRid(_canvasItem);
        RenderingServer.FreeRid(SubViewportRid);
    }

    public Vector2I UpdateViewport()
    {
        Vector2I viewportSize = _parentViewport is Window window
            ? window.Size
            : (_parentViewport as SubViewport)?.Size ?? Vector2I.Zero;

        Transform2D finalTransform = _parentViewport.GetFinalTransform();
        if (_subViewportSize != viewportSize || _finalTransform != finalTransform)
        {
            _subViewportSize = viewportSize;
            _finalTransform = finalTransform;

            RenderingServer.ViewportSetSize(SubViewportRid, viewportSize.X, viewportSize.Y);
            Rid viewportTexture = RenderingServer.ViewportGetTexture(SubViewportRid);
            RenderingServer.CanvasItemClear(_canvasItem);
            RenderingServer.CanvasItemSetTransform(_canvasItem, finalTransform.AffineInverse());
            RenderingServer.CanvasItemAddTextureRect(
                _canvasItem,
                new Rect2(0, 0, viewportSize.X, viewportSize.Y),
                viewportTexture);
        }

        return viewportSize;
    }

    private static Rid CreateSubViewport(Rid parentViewport)
    {
        Rid viewport = RenderingServer.ViewportCreate();
        RenderingServer.ViewportSetTransparentBackground(viewport, true);
        RenderingServer.ViewportSetUpdateMode(viewport, RenderingServer.ViewportUpdateMode.Always);
        RenderingServer.ViewportSetClearMode(viewport, RenderingServer.ViewportClearMode.Never);
        RenderingServer.ViewportSetActive(viewport, true);
        RenderingServer.ViewportSetParentViewport(viewport, parentViewport);
        return viewport;
    }
}
