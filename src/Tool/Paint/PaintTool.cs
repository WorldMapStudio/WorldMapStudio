using System;
using System.Linq;
using Godot;
using ImGuiNET;
using GVector3 = Godot.Vector3;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>Projects a circular brush onto the selected entity's bound <see cref="PaintImage"/>, via
/// its <see cref="ImageComponent"/> placement.</summary>
public sealed class PaintTool : ITool
{
    private readonly SelectionSystem _selection;
    private readonly SceneEntityRegistry _scene;
    private readonly LandscapeSystem _landscape;
    private readonly Brush _brush;
    private readonly ImagePaintOptions _options;
    private readonly BrushSurface _surface;
    private readonly BrushViewport _viewport;

    private ImagePaintTarget? _target;

    public PaintTool(ToolContext context, Brush brush, ImagePaintOptions options)
    {
        _selection = context.Selection;
        _scene = context.Scene;
        _landscape = context.Editor.Landscape;
        _brush = brush;
        _options = options;
        _surface = new BrushSurface(new TerrainProbe(_landscape));
        _viewport = new BrushViewport(brush, _surface, context.Sessions);
    }

    public string Name => "Paint";

    public bool CapturesMouse => _viewport.IsStroking;

    public void DrawToolbar()
    {
        BrushControls.Draw(_brush, strengthLabel: "Opacity", invertLabel: "Erase");
        ImGui.SameLine();

        bool colorAvailable = (ActiveTarget()?.Image?.Components ?? 1) > 1;
        if (!colorAvailable)
        {
            ImGui.BeginDisabled();
        }

        ImGui.SetNextItemWidth(160.0f);
        var colorValue = new NVector3(_options.Color.R, _options.Color.G, _options.Color.B);
        if (ImGui.ColorEdit3("Color", ref colorValue))
        {
            _options.Color = new Color(colorValue.X, colorValue.Y, colorValue.Z);
        }

        if (!colorAvailable)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();

        bool objectAvailable = ActiveTarget()?.DisplayLayer?.DisplayMode == ImageDisplayMode.Object;
        if (!objectAvailable)
        {
            ImGui.BeginDisabled();
        }

        bool paintOnObject = _options.PaintOnObject;
        if (ImGui.Checkbox("Paint on Object", ref paintOnObject))
        {
            _options.PaintOnObject = paintOnObject;
        }

        if (!objectAvailable)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        ImGui.TextDisabled(ActiveTarget()?.Owner?.DisplayName ?? "No image selected");
    }

    public void UpdateViewport(in ViewportContext context)
    {
        ImageComponent? component = ActiveTarget();
        if (component == null)
        {
            _target = null;
            _viewport.Update(context, null);
            return;
        }

        if (_target?.Component != component)
        {
            _target = new ImagePaintTarget(component, _options, _landscape);
            _surface.Accepts = _target.Covers;
        }

        bool onObject = _options.PaintOnObject && component.DisplayLayer?.DisplayMode == ImageDisplayMode.Object;
        _surface.Plane = onObject ? component.Owner!.Transform : null;

        _viewport.Update(context, _target, projector => DrawTargetOutline(component, projector));
    }

    public void Deactivate()
    {
        _viewport.Finish(record: true);
    }

    private ImageComponent? ActiveTarget() =>
        _selection.Selected.OfType<SceneEntity>()
            .Where(entity => _scene.Contains(entity))
            .Select(entity => entity.Component<ImageComponent>())
            .FirstOrDefault(component => component != null);

    private void DrawTargetOutline(ImageComponent target, in ViewportProjector projector)
    {
        GVector3 half = new(target.WorldSizeX * 0.5f, 0.0f, target.WorldSizeZ * 0.5f);
        GVector3[] local =
        [
            new GVector3(-half.X, 0.0f, -half.Z),
            new GVector3(half.X, 0.0f, -half.Z),
            new GVector3(half.X, 0.0f, half.Z),
            new GVector3(-half.X, 0.0f, half.Z),
        ];

        Span<NVector2> screen = stackalloc NVector2[4];
        for (int i = 0; i < local.Length; i++)
        {
            GVector3 world = target.Owner!.Transform * local[i];
            if (!projector.TryProject(_surface.Lift(world), out screen[i]))
            {
                return;
            }
        }

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint color = ImGui.GetColorU32(new NVector4(1.0f, 0.62f, 0.20f, 0.95f));
        for (int i = 0; i < screen.Length; i++)
        {
            drawList.AddLine(screen[i], screen[(i + 1) % screen.Length], color, 2.0f);
        }
    }
}
