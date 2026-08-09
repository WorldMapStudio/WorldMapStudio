using System;
using System.Linq;
using Godot;
using ImGuiNET;
using GVector2 = Godot.Vector2;
using GVector3 = Godot.Vector3;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>Projects a circular brush onto the selected <see cref="DrawingTargetEntity"/>.</summary>
public sealed class PaintTool : ITool
{
    private readonly SelectionSystem _selection;
    private readonly SceneEntityRegistry _scene;
    private readonly EditSessionManager _sessions;

    private float _radius = 4.0f;
    private float _opacity = 0.35f;
    private bool _erase;
    private bool _painting;
    private DrawingTargetEntity? _strokeTarget;
    private byte[]? _strokeBefore;

    public PaintTool(ToolContext context)
    {
        _selection = context.Selection;
        _scene = context.Scene;
        _sessions = context.Sessions;
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
        ImGui.TextDisabled(ActiveTarget()?.DisplayName ?? "No drawing target selected");
    }

    public void UpdateViewport(in ViewportContext context)
    {
        DrawingTargetEntity? target = ActiveTarget();
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
                _scene.Touch(target);
            }
        }
    }

    public void Deactivate()
    {
        FinishStroke(record: true);
    }

    private DrawingTargetEntity? ActiveTarget() =>
        _selection.Selected.OfType<DrawingTargetEntity>().FirstOrDefault(entity => _scene.Contains(entity));

    private void FinishStroke(bool record)
    {
        if (!_painting)
        {
            return;
        }

        _painting = false;
        DrawingTargetEntity? target = _strokeTarget;
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

    private static bool TryHit(DrawingTargetEntity target, in ViewportContext context, out GVector3 local)
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
        Transform3D inverse = target.Transform.AffineInverse();
        GVector3 localOrigin = inverse * rayOrigin;
        GVector3 localDir = inverse.Basis * rayDir;

        if (Mathf.Abs(localDir.Y) < 1e-6f)
        {
            local = default;
            return false;
        }

        float t = -localOrigin.Y / localDir.Y;
        if (t < 0.0f)
        {
            local = default;
            return false;
        }

        local = localOrigin + (localDir * t);
        return Mathf.Abs(local.X) <= target.WorldSizeX * 0.5f &&
               Mathf.Abs(local.Z) <= target.WorldSizeZ * 0.5f;
    }

    private static void DrawTargetOutline(DrawingTargetEntity target, Camera3D camera, NVector2 imageMin)
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
            if (!ObjectSelection.WorldToScreen(camera, target.Transform * local[i], imageMin, out screen[i]))
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

    private void DrawBrush(DrawingTargetEntity target, GVector3 local, Camera3D camera, NVector2 imageMin)
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
            GVector3 world = target.Transform * point;
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
