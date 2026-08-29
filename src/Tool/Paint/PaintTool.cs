using System;
using System.Linq;
using Godot;
using ImGuiNET;
using GVector2 = Godot.Vector2;
using GVector3 = Godot.Vector3;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>Projects a circular brush onto the selected entity's <see cref="DrawingTargetComponent"/>.</summary>
public sealed class PaintTool : ITool
{
    private readonly SelectionSystem _selection;
    private readonly SceneEntityRegistry _scene;
    private readonly EditSessionManager _sessions;
    private readonly TerrainProbe _terrain;

    private float _radius = 4.0f;
    private float _opacity = 0.35f;
    private bool _erase;
    private bool _painting;
    private DrawingTargetComponent? _strokeTarget;
    private byte[]? _strokeBefore;

    public PaintTool(ToolContext context)
    {
        _selection = context.Selection;
        _scene = context.Scene;
        _sessions = context.Sessions;
        _terrain = new TerrainProbe(context.Scene);
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
        ImGui.TextDisabled(ActiveTarget()?.Owner?.DisplayName ?? "No drawing target selected");
    }

    public void UpdateViewport(in ViewportContext context)
    {
        DrawingTargetComponent? target = ActiveTarget();
        if (target == null || context.CameraFlying)
        {
            FinishStroke(record: false);
            return;
        }

        DrawTargetOutline(target, context.Camera, context.ImageMin);

        bool hit = TryHit(target, context, out GVector3 local);
        if (hit)
        {
            DrawBrush(target, local, context.Camera, context.ImageMin);
        }

        if (!_painting && context.Hovered && hit && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _painting = true;
            _strokeTarget = target;
            _strokeBefore = target.CopyPixels();
        }

        if (_painting)
        {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                FinishStroke(record: true);
                return;
            }

            if (_strokeTarget == target && hit && target.Paint(local, _radius, _opacity, _erase))
            {
                _scene.Touch(target.Owner!);
            }
        }
    }

    public void Deactivate()
    {
        FinishStroke(record: true);
    }

    private DrawingTargetComponent? ActiveTarget() =>
        _selection.Selected.OfType<SceneEntity>()
            .Where(entity => _scene.Contains(entity))
            .Select(entity => entity.Component<DrawingTargetComponent>())
            .FirstOrDefault(component => component != null);

    private void FinishStroke(bool record)
    {
        if (!_painting)
        {
            return;
        }

        _painting = false;
        DrawingTargetComponent? target = _strokeTarget;
        byte[]? before = _strokeBefore;
        _strokeTarget = null;
        _strokeBefore = null;

        if (!record || target == null || before == null)
        {
            return;
        }

        byte[] after = target.CopyPixels();
        if (!before.SequenceEqual(after))
        {
            _sessions.Record(new SetDrawingTargetPixelsCommand(target, before, after));
        }
    }

    private bool TryHit(DrawingTargetComponent target, in ViewportContext context, out GVector3 local)
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

        if (TryHitTerrain(target, rayOrigin, rayDir, out local))
        {
            return true;
        }

        return TryHitFallbackPlane(target, rayOrigin, rayDir, out local);
    }

    private bool TryHitTerrain(DrawingTargetComponent target, GVector3 rayOrigin, GVector3 rayDir, out GVector3 local)
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

    private static bool TryHitFallbackPlane(DrawingTargetComponent target, GVector3 rayOrigin, GVector3 rayDir, out GVector3 local)
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

    private static bool TargetContains(DrawingTargetComponent target, GVector3 local) =>
        Mathf.Abs(local.X) <= target.WorldSizeX * 0.5f &&
        Mathf.Abs(local.Z) <= target.WorldSizeZ * 0.5f;

    private GVector3 TerrainPoint(DrawingTargetComponent target, GVector3 local)
    {
        GVector3 world = target.Owner!.Transform * new GVector3(local.X, 0.0f, local.Z);
        return _terrain.DropToHeight(world);
    }

    private void DrawTargetOutline(DrawingTargetComponent target, Camera3D camera, NVector2 imageMin)
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
            if (!ObjectSelection.WorldToScreen(camera, TerrainPoint(target, local[i]), imageMin, out screen[i]))
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

    private void DrawBrush(DrawingTargetComponent target, GVector3 local, Camera3D camera, NVector2 imageMin)
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
            GVector3 world = TerrainPoint(target, point);
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
