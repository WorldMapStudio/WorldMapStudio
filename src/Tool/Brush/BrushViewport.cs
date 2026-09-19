using System;
using Godot;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// The per-frame driver a brush tool delegates to: finds the surface point under the pointer, draws the
/// cursor, and turns mouse down / held / up into a <see cref="BrushStroke"/> on the tool's
/// <see cref="IStrokeTarget"/>. Keyboard: <c>[</c> and <c>]</c> resize the brush, and holding Ctrl as a
/// stroke begins inverts it.
/// </summary>
public sealed class BrushViewport
{
    private const float RadiusKeyStep = 1.1f;
    private const int CircleSegments = 48;
    private const float InnerCircleMinHardness = 0.02f;

    private readonly Brush _brush;
    private readonly BrushSurface _surface;
    private readonly EditSessionManager _sessions;

    private BrushStroke? _stroke;

    public BrushViewport(Brush brush, BrushSurface surface, EditSessionManager sessions)
    {
        _brush = brush;
        _surface = surface;
        _sessions = sessions;
    }

    public bool IsStroking => _stroke is { IsActive: true };

    /// <summary>Where the pointer last hit the surface, for tools that read out what is under it.</summary>
    public bool HasHit { get; private set; }

    public Vector3 HitPoint { get; private set; }

    /// <summary>
    /// Runs one frame. A null <paramref name="target"/> means there is nothing to paint right now, which
    /// ends any stroke. <paramref name="overlay"/> draws the tool's own extras before the cursor.
    /// </summary>
    public void Update(in ViewportContext context, IStrokeTarget? target, Action<ViewportProjector>? overlay = null)
    {
        HasHit = false;
        if (_stroke is { IsActive: true } active && (target == null || !ReferenceEquals(active.Target, target) || context.CameraFlying))
        {
            Finish(record: true);
        }

        var projector = new ViewportProjector(context.Camera, context.ImageMin, context.ImageSize);
        overlay?.Invoke(projector);

        if (target == null || context.CameraFlying)
        {
            return;
        }

        HasHit = _surface.TryHit(context, out Vector3 hit);
        HitPoint = hit;

        if (context.Hovered)
        {
            HandleKeys();
        }

        bool invert = IsStroking ? _stroke!.IsInverted : ImGui.GetIO().KeyCtrl != _brush.Invert;
        if (HasHit)
        {
            DrawCursor(hit, invert, projector);
        }

        if (!IsStroking && context.Hovered && HasHit && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            var stroke = new BrushStroke(_brush, target, _sessions);
            if (stroke.Begin(invert: ImGui.GetIO().KeyCtrl))
            {
                _stroke = stroke;
            }
        }

        if (_stroke is not { IsActive: true } current)
        {
            return;
        }

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            Finish(record: true);
            return;
        }

        if (HasHit)
        {
            current.Advance(hit, Time.GetTicksMsec() / 1000.0);
        }
    }

    /// <summary>Ends any stroke in progress.</summary>
    public void Finish(bool record)
    {
        _stroke?.Finish(record);
        _stroke = null;
    }

    private void HandleKeys()
    {
        if (ImGui.GetIO().WantTextInput)
        {
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.LeftBracket))
        {
            _brush.Radius /= RadiusKeyStep;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.RightBracket))
        {
            _brush.Radius *= RadiusKeyStep;
        }
    }

    private void DrawCursor(Vector3 center, bool invert, in ViewportProjector projector)
    {
        uint color = ImGui.GetColorU32(invert
            ? new NVector4(1.0f, 0.35f, 0.25f, 0.95f)
            : new NVector4(0.2f, 0.75f, 1.0f, 0.95f));

        DrawCircle(center, _brush.Radius, color, 2.0f, projector);
        if (_brush.Hardness > InnerCircleMinHardness)
        {
            DrawCircle(center, _brush.Radius * _brush.Hardness, color & 0x80FFFFFF, 1.0f, projector);
        }
    }

    private void DrawCircle(Vector3 center, float radius, uint color, float thickness, in ViewportProjector projector)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        NVector2? previous = null;
        for (int i = 0; i <= CircleSegments; i++)
        {
            float angle = Mathf.Tau * i / CircleSegments;
            var point = new Vector3(
                center.X + (Mathf.Cos(angle) * radius),
                center.Y,
                center.Z + (Mathf.Sin(angle) * radius));
            if (!projector.TryProject(_surface.Lift(point), out NVector2 screen))
            {
                previous = null;
                continue;
            }

            if (previous is { } p)
            {
                drawList.AddLine(p, screen, color, thickness);
            }

            previous = screen;
        }
    }
}
