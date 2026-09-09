using System;
using System.Linq;
using Godot;
using ImGuiNET;
using GVector2 = Godot.Vector2;
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
    private readonly EditSessionManager _sessions;
    private readonly TerrainProbe _terrain;

    private float _radius = 4.0f;
    private float _opacity = 0.35f;
    private Color _color = Colors.White;
    private bool _erase;
    private bool _paintOnObject = true;
    private bool _painting;
    private ImageComponent? _strokeTarget;
    private PaintImage? _strokeImage;

    public PaintTool(ToolContext context)
    {
        _selection = context.Selection;
        _scene = context.Scene;
        _sessions = context.Sessions;
        _terrain = new TerrainProbe(context.Scene, context.Editor.Landscape);
    }

    public string Name => "Paint";

    public bool CapturesMouse => _painting;

    public void DrawToolbar()
    {
        ImGui.SetNextItemWidth(120.0f);
        ImGui.DragFloat("Radius", ref _radius, 0.1f, 0.1f, 512.0f);
        ImGui.SameLine();

        ImGui.SetNextItemWidth(120.0f);
        ImGui.DragFloat("Opacity", ref _opacity, 0.01f, 0.01f, 1.0f);
        ImGui.SameLine();

        ImGui.Checkbox("Erase", ref _erase);
        ImGui.SameLine();

        bool colorAvailable = (ActiveTarget()?.Image?.Components ?? 1) > 1;
        if (!colorAvailable)
        {
            ImGui.BeginDisabled();
        }

        ImGui.SetNextItemWidth(160.0f);
        var colorValue = new NVector3(_color.R, _color.G, _color.B);
        if (ImGui.ColorEdit3("Color", ref colorValue))
        {
            _color = new Color(colorValue.X, colorValue.Y, colorValue.Z);
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

        ImGui.Checkbox("Paint on Object", ref _paintOnObject);

        if (!objectAvailable)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        ImGui.TextDisabled(ActiveTarget()?.Owner?.DisplayName ?? "No image selected");
    }

    public void UpdateViewport(in ViewportContext context)
    {
        ImageComponent? target = ActiveTarget();
        if (target == null || context.CameraFlying)
        {
            FinishStroke(record: false);
            return;
        }

        bool onObject = _paintOnObject && target.DisplayLayer?.DisplayMode == ImageDisplayMode.Object;

        DrawTargetOutline(target, onObject, context.Camera, context.ImageMin);

        bool hit = TryHit(target, onObject, context, out GVector3 local);
        if (hit)
        {
            DrawBrush(target, onObject, local, context.Camera, context.ImageMin);
        }

        if (!_painting && context.Hovered && hit && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && target.Image is { } image)
        {
            _painting = true;
            _strokeTarget = target;
            _strokeImage = image;
            image.BeginStroke();
        }

        if (_painting)
        {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                FinishStroke(record: true);
                return;
            }

            if (_strokeTarget == target && hit && target.Paint(local, _radius, _color, _opacity, _erase))
            {
                _scene.Touch(target.Owner!);
            }
        }
    }

    public void Deactivate()
    {
        FinishStroke(record: true);
    }

    private ImageComponent? ActiveTarget() =>
        _selection.Selected.OfType<SceneEntity>()
            .Where(entity => _scene.Contains(entity))
            .Select(entity => entity.Component<ImageComponent>())
            .FirstOrDefault(component => component != null);

    private void FinishStroke(bool record)
    {
        if (!_painting)
        {
            return;
        }

        _painting = false;
        ImageComponent? target = _strokeTarget;
        PaintImage? image = _strokeImage;
        _strokeTarget = null;
        _strokeImage = null;

        // Always drains the stroke's per-chunk tracking, even when not recording — otherwise the next
        // stroke's "before" snapshots would start from whatever an abandoned stroke left in progress.
        var edits = image?.EndStroke();

        if (!record || target == null || image == null || edits is not { Count: > 0 })
        {
            return;
        }

        _sessions.Record(new PaintImageChunksCommand(image, edits, target.AffectedEntities, $"Paint {image.Name}"));
    }

    private bool TryHit(ImageComponent target, bool onObject, in ViewportContext context, out GVector3 local)
    {
        NVector2 mouse = ImGui.GetMousePos();
        GVector2 viewport = new(mouse.X - context.ImageMin.X, mouse.Y - context.ImageMin.Y);
        if (viewport.X < 0.0f || viewport.Y < 0.0f || viewport.X > context.ImageSize.X || viewport.Y > context.ImageSize.Y)
        {
            local = default;
            return false;
        }

        GVector3 rayOrigin = context.Camera.ProjectRayOrigin(viewport);
        GVector3 rayDir = context.Camera.ProjectRayNormal(viewport);

        if (onObject)
        {
            return TryHitObject(target, rayOrigin, rayDir, out local);
        }

        if (TryHitTerrain(target, rayOrigin, rayDir, out local))
        {
            return true;
        }

        return TryHitFallbackPlane(target, rayOrigin, rayDir, out local);
    }

    /// <summary>Object display mode paints directly onto its own mesh instead of projecting through
    /// the terrain — reuses the same ray-vs-triangle test <see cref="IMeshPickable"/> click-selection
    /// already gets (<see cref="SceneEntity.TryPickGeometry"/>) rather than inventing separate plane
    /// math, since the built object is just an ordinary mesh child.</summary>
    private static bool TryHitObject(ImageComponent target, GVector3 rayOrigin, GVector3 rayDir, out GVector3 local)
    {
        local = default;
        if (!target.Owner!.TryPickGeometry(rayOrigin, rayDir, out float t, out _))
        {
            return false;
        }

        GVector3 world = rayOrigin + (rayDir * t);
        GVector3 candidate = target.Owner!.Transform.AffineInverse() * world;
        if (!TargetContains(target, candidate))
        {
            return false;
        }

        local = candidate;
        return true;
    }

    private bool TryHitTerrain(ImageComponent target, GVector3 rayOrigin, GVector3 rayDir, out GVector3 local)
    {
        local = default;
        if (!_terrain.TryHit(rayOrigin, rayDir, out GVector3 world))
        {
            return false;
        }

        GVector3 candidate = target.Owner!.Transform.AffineInverse() * world;
        if (!TargetContains(target, candidate))
        {
            return false;
        }

        local = candidate;
        return true;
    }

    private static bool TryHitFallbackPlane(ImageComponent target, GVector3 rayOrigin, GVector3 rayDir, out GVector3 local)
    {
        if (Mathf.Abs(rayDir.Y) < 1e-6f)
        {
            local = default;
            return false;
        }

        float t = -rayOrigin.Y / rayDir.Y;
        if (t < 0.0f)
        {
            local = default;
            return false;
        }

        local = target.Owner!.Transform.AffineInverse() * (rayOrigin + (rayDir * t));
        return TargetContains(target, local);
    }

    private static bool TargetContains(ImageComponent target, GVector3 local) =>
        Mathf.Abs(local.X) <= target.WorldSizeX * 0.5f &&
        Mathf.Abs(local.Z) <= target.WorldSizeZ * 0.5f;

    /// <summary>Where a local footprint point actually lands for display — the object's own flat
    /// surface when painting targets it, or dropped onto the terrain otherwise. Must match whatever
    /// <see cref="TryHit"/> just hit-tested against, or the outline/brush would lie about where a
    /// click actually lands.</summary>
    private GVector3 SurfacePoint(ImageComponent target, GVector3 local, bool onObject)
    {
        GVector3 world = target.Owner!.Transform * new GVector3(local.X, 0.0f, local.Z);
        return onObject ? world : _terrain.DropToHeight(world);
    }

    private void DrawTargetOutline(ImageComponent target, bool onObject, Camera3D camera, NVector2 imageMin)
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
            if (!ObjectSelection.WorldToScreen(camera, SurfacePoint(target, local[i], onObject), imageMin, out screen[i]))
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

    private void DrawBrush(ImageComponent target, bool onObject, GVector3 local, Camera3D camera, NVector2 imageMin)
    {
        const int Segments = 48;

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint color = ImGui.GetColorU32(_erase
            ? new NVector4(1.0f, 0.35f, 0.25f, 0.95f)
            : new NVector4(0.2f, 0.75f, 1.0f, 0.95f));

        NVector2? previous = null;
        for (int i = 0; i <= Segments; i++)
        {
            float angle = Mathf.Tau * i / Segments;
            GVector3 point = new(
                local.X + (Mathf.Cos(angle) * _radius),
                0.0f,
                local.Z + (Mathf.Sin(angle) * _radius));
            GVector3 world = SurfacePoint(target, point, onObject);
            if (!ObjectSelection.WorldToScreen(camera, world, imageMin, out NVector2 screen))
            {
                previous = null;
                continue;
            }

            if (previous is { } p)
            {
                drawList.AddLine(p, screen, color, 2.0f);
            }

            previous = screen;
        }
    }
}
