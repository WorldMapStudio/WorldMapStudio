using System;
using Godot;
using ImGuiNET;
using GVector2 = Godot.Vector2;
using GVector3 = Godot.Vector3;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// The kind of manipulation a <see cref="TransformGizmo"/> currently performs.
/// </summary>
public enum GizmoOperation
{
    Translate,
    Rotate,
}

/// <summary>
/// A generic, self-contained 3D transform gizmo drawn in screen space over a Godot
/// <see cref="SubViewport"/> image using ImGui's draw list, in the style of the Unreal
/// editor gizmo. It supports translation (axis arrows + plane handles) and rotation
/// (axis rings) across all three axes and does not know anything about what it edits:
/// callers hand it a <see cref="Camera3D"/>, the on-screen rectangle of the rendered
/// viewport, and a <see cref="Transform3D"/> to mutate.
///
/// Because it draws in screen space via projection, the gizmo keeps a constant on-screen
/// size regardless of camera distance, exactly like a real editor gizmo.
/// </summary>
public sealed class TransformGizmo
{
    // Target on-screen size, in pixels, of the gizmo's axes/rings.
    private const float ScreenSize = 90.0f;
    private const float AxisPickWidth = 7.0f;
    private const float RingPickWidth = 7.0f;
    private const int RingSegments = 64;

    // Below this |ray·planeNormal| the drag ray grazes the plane and the solution is unstable;
    // ~0.1 corresponds to roughly 6 degrees and is only reached by dragging far off-screen.
    private const float GrazingCosine = 0.1f;

    private static readonly NVector4[] AxisColors =
    [
        new(0.91f, 0.24f, 0.28f, 1.0f), // X
        new(0.49f, 0.78f, 0.16f, 1.0f), // Y
        new(0.22f, 0.49f, 0.93f, 1.0f), // Z
    ];

    private static readonly NVector4 HighlightColor = new(1.0f, 0.79f, 0.11f, 1.0f);

    private enum DragKind
    {
        None,
        Axis,
        Plane,
        Ring,
    }

    /// <summary>Which manipulation the gizmo currently offers. Toggle freely between frames.</summary>
    public GizmoOperation Operation { get; set; } = GizmoOperation.Translate;

    /// <summary>
    /// When true the gizmo aligns to the target's own axes (local space); when false it
    /// aligns to world axes. Local space makes rotations visually obvious because the
    /// gizmo rotates together with the object.
    /// </summary>
    public bool LocalSpace { get; set; } = true;

    /// <summary>True while the user is actively dragging a handle.</summary>
    public bool IsUsing => _dragKind != DragKind.None;

    /// <summary>True while a handle is highlighted under the cursor (but not yet grabbed).</summary>
    public bool IsHovered { get; private set; }

    private DragKind _dragKind;
    private int _activeAxis = -1;
    private int _hoverAxis = -1;
    private DragKind _hoverKind = DragKind.None;

    // Drag state, captured once at grab time so a moving/rotating target can't feed back.
    private GVector3 _dragStartOrigin;
    private GVector3 _dragAxisWorld;
    private GVector3 _dragReference;
    private Basis _dragStartBasis;
    private GVector3 _ringLastVec;
    private float _ringAccum;

    /// <summary>
    /// Draw the gizmo and process one frame of interaction against <paramref name="transform"/>.
    /// </summary>
    /// <param name="camera">Camera the viewport is rendered with (used for projection).</param>
    /// <param name="imageMin">Top-left of the rendered viewport image, in ImGui screen coordinates.</param>
    /// <param name="imageSize">Size of the rendered viewport image, in pixels.</param>
    /// <param name="interactive">Whether new drags may begin this frame (e.g. the image is hovered).</param>
    /// <param name="transform">The transform to manipulate; mutated in place.</param>
    /// <returns>True if the transform changed this frame.</returns>
    public bool Manipulate(Camera3D camera, NVector2 imageMin, NVector2 imageSize, bool interactive, ref Transform3D transform)
    {
        IsHovered = false;
        _hoverAxis = -1;
        _hoverKind = DragKind.None;

        if (imageSize.X < 1.0f || imageSize.Y < 1.0f || camera.IsPositionBehind(transform.Origin))
        {
            _dragKind = DragKind.None;
            return false;
        }

        GVector3 origin = transform.Origin;
        float worldScale = WorldSizePerScreenPixel(camera, origin, (int)imageSize.Y) * ScreenSize;

        // Resolve the three axis directions for the current space.
        Span<GVector3> axes = [AxisDir(transform, 0), AxisDir(transform, 1), AxisDir(transform, 2)];

        NVector2 mouse = ImGui.GetMousePos();
        GVector2 mouseLocal = new(mouse.X - imageMin.X, mouse.Y - imageMin.Y);
        GVector3 rayOrigin = camera.ProjectRayOrigin(mouseLocal);
        GVector3 rayDir = camera.ProjectRayNormal(mouseLocal);

        bool changed = false;
        if (_dragKind != DragKind.None)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                changed = ContinueDrag(camera, ref transform, origin, rayOrigin, rayDir);
            }
            else
            {
                _dragKind = DragKind.None;
                _activeAxis = -1;
            }
        }
        else
        {
            HitTest(camera, imageMin, origin, axes, worldScale, mouse);
            if (interactive && _hoverKind != DragKind.None && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                BeginDrag(camera, transform, origin, axes, rayOrigin, rayDir);
            }
        }

        Draw(camera, imageMin, origin, axes, worldScale);
        return changed;
    }

    private GVector3 AxisDir(in Transform3D transform, int index)
    {
        if (LocalSpace)
        {
            GVector3 col = index == 0 ? transform.Basis.X : index == 1 ? transform.Basis.Y : transform.Basis.Z;
            return col.Normalized();
        }

        return index == 0 ? GVector3.Right : index == 1 ? GVector3.Up : GVector3.Back;
    }

    // ---- Interaction -------------------------------------------------------

    private void HitTest(Camera3D camera, NVector2 imageMin, GVector3 origin, Span<GVector3> axes, float worldScale, NVector2 mouse)
    {
        if (Operation == GizmoOperation.Translate)
        {
            // Plane handles take priority: they sit near the centre and are easy to miss otherwise.
            for (int k = 0; k < 3; k++)
            {
                if (PointInQuad(mouse, PlaneQuad(camera, imageMin, origin, axes, worldScale, k)))
                {
                    _hoverKind = DragKind.Plane;
                    _hoverAxis = k;
                    IsHovered = true;
                    return;
                }
            }

            float best = AxisPickWidth;
            for (int i = 0; i < 3; i++)
            {
                if (!Project(camera, imageMin, origin, out NVector2 c) ||
                    !Project(camera, imageMin, origin + axes[i] * worldScale, out NVector2 tip))
                {
                    continue;
                }

                float d = DistanceToSegment(mouse, c, tip);
                if (d < best)
                {
                    best = d;
                    _hoverKind = DragKind.Axis;
                    _hoverAxis = i;
                    IsHovered = true;
                }
            }
        }
        else
        {
            float best = RingPickWidth;
            for (int i = 0; i < 3; i++)
            {
                float d = DistanceToRing(camera, imageMin, origin, axes, worldScale, i, mouse);
                if (d < best)
                {
                    best = d;
                    _hoverKind = DragKind.Ring;
                    _hoverAxis = i;
                    IsHovered = true;
                }
            }
        }
    }

    private void BeginDrag(Camera3D camera, in Transform3D transform, GVector3 origin, Span<GVector3> axes, GVector3 rayOrigin, GVector3 rayDir)
    {
        _dragKind = _hoverKind;
        _activeAxis = _hoverAxis;
        _dragStartOrigin = origin;
        _dragStartBasis = transform.Basis;

        switch (_dragKind)
        {
            case DragKind.Axis:
                _dragAxisWorld = axes[_activeAxis];
                _dragReference = RayPlane(rayOrigin, rayDir, origin, AxisDragPlaneNormal(_dragAxisWorld, origin, camera));
                break;

            case DragKind.Plane:
                _dragAxisWorld = axes[_activeAxis]; // plane normal
                _dragReference = RayPlane(rayOrigin, rayDir, origin, _dragAxisWorld);
                break;

            case DragKind.Ring:
                _dragAxisWorld = axes[_activeAxis];
                GVector3 hit = RayPlane(rayOrigin, rayDir, origin, _dragAxisWorld);
                _ringLastVec = InPlane(hit - origin, _dragAxisWorld).Normalized();
                _ringAccum = 0.0f;
                break;
        }
    }

    private bool ContinueDrag(Camera3D camera, ref Transform3D transform, GVector3 origin, GVector3 rayOrigin, GVector3 rayDir)
    {
        switch (_dragKind)
        {
            case DragKind.Axis:
            {
                // Intersect the ray with a camera-facing plane through the axis, then project
                // the movement onto the axis. Far more stable than a line-to-line closest
                // point, which reverses as the ray nears the axis.
                GVector3 planeNormal = AxisDragPlaneNormal(_dragAxisWorld, _dragStartOrigin, camera);
                float denom = rayDir.Dot(planeNormal);
                if (Mathf.Abs(denom) < GrazingCosine)
                {
                    // The cursor has been dragged so far that the ray grazes the drag plane;
                    // hold the last position instead of letting it snap wildly.
                    return false;
                }

                float t = (_dragStartOrigin - rayOrigin).Dot(planeNormal) / denom;
                GVector3 current = rayOrigin + rayDir * t;
                float delta = (current - _dragReference).Dot(_dragAxisWorld);
                transform.Origin = _dragStartOrigin + _dragAxisWorld * delta;
                return true;
            }

            case DragKind.Plane:
            {
                GVector3 current = RayPlane(rayOrigin, rayDir, _dragStartOrigin, _dragAxisWorld);
                transform.Origin = _dragStartOrigin + (current - _dragReference);
                return true;
            }

            case DragKind.Ring:
            {
                GVector3 hit = RayPlane(rayOrigin, rayDir, origin, _dragAxisWorld);
                GVector3 vec = InPlane(hit - origin, _dragAxisWorld).Normalized();
                if (vec.LengthSquared() < 1e-6f)
                {
                    return false;
                }

                // Accumulate incrementally so rotations past 180 degrees don't wrap.
                _ringAccum += SignedAngle(_ringLastVec, vec, _dragAxisWorld);
                _ringLastVec = vec;
                transform.Basis = new Basis(_dragAxisWorld, _ringAccum) * _dragStartBasis;
                return true;
            }
        }

        return false;
    }

    // ---- Drawing -----------------------------------------------------------

    private void Draw(Camera3D camera, NVector2 imageMin, GVector3 origin, Span<GVector3> axes, float worldScale)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        if (!Project(camera, imageMin, origin, out NVector2 center))
        {
            return;
        }

        if (Operation == GizmoOperation.Translate)
        {
            DrawTranslate(drawList, camera, imageMin, origin, axes, worldScale, center);
        }
        else
        {
            DrawRotate(drawList, camera, imageMin, origin, axes, worldScale, center);
        }

        // Central pivot dot, drawn last so it always caps the axes cleanly.
        drawList.AddCircleFilled(center, 4.0f, ImGui.GetColorU32(new NVector4(0.92f, 0.92f, 0.92f, 1.0f)));
        drawList.AddCircle(center, 4.0f, ImGui.GetColorU32(new NVector4(0.1f, 0.1f, 0.1f, 0.9f)), 0, 1.5f);
    }

    private void DrawTranslate(ImDrawListPtr drawList, Camera3D camera, NVector2 imageMin, GVector3 origin, Span<GVector3> axes, float worldScale, NVector2 center)
    {
        // Faint two-axis plane handles first, so the arrows sit on top of them.
        for (int k = 0; k < 3; k++)
        {
            bool hot = (_dragKind == DragKind.Plane && _activeAxis == k) ||
                       (_dragKind == DragKind.None && _hoverKind == DragKind.Plane && _hoverAxis == k);
            NVector2[] quad = PlaneQuad(camera, imageMin, origin, axes, worldScale, k);
            NVector4 fill = hot ? HighlightColor : AxisColors[k];
            drawList.AddQuadFilled(quad[0], quad[1], quad[2], quad[3], ImGui.GetColorU32(fill with { W = hot ? 0.45f : 0.22f }));
            drawList.AddQuad(quad[0], quad[1], quad[2], quad[3], ImGui.GetColorU32(fill with { W = hot ? 1.0f : 0.7f }), 1.5f);
        }

        // Draw arrows far-to-near so overlaps look right.
        Span<int> order = [0, 1, 2];
        SortByDepth(camera, origin, axes, worldScale, order);
        foreach (int i in order)
        {
            bool hot = (_dragKind == DragKind.Axis && _activeAxis == i) ||
                       (_dragKind == DragKind.None && _hoverKind == DragKind.Axis && _hoverAxis == i);
            if (!Project(camera, imageMin, origin + axes[i] * worldScale, out NVector2 tip))
            {
                continue;
            }

            NVector4 color = hot ? HighlightColor : AxisColors[i];
            uint col = ImGui.GetColorU32(color);

            NVector2 dir = Normalize(tip - center);
            NVector2 perp = new(-dir.Y, dir.X);
            float thickness = hot ? 4.0f : 3.0f;
            float headLen = 15.0f;
            float headWidth = 6.0f;
            NVector2 headBase = tip - dir * headLen;

            drawList.AddLine(center, headBase, col, thickness);
            drawList.AddTriangleFilled(tip, headBase + perp * headWidth, headBase - perp * headWidth, col);
        }
    }

    private void DrawRotate(ImDrawListPtr drawList, Camera3D camera, NVector2 imageMin, GVector3 origin, Span<GVector3> axes, float worldScale, NVector2 center)
    {
        for (int i = 0; i < 3; i++)
        {
            bool hot = (_dragKind == DragKind.Ring && _activeAxis == i) ||
                       (_dragKind == DragKind.None && _hoverKind == DragKind.Ring && _hoverAxis == i);
            uint col = ImGui.GetColorU32(hot ? HighlightColor : AxisColors[i]);

            GVector3 u = axes[(i + 1) % 3];
            GVector3 v = axes[(i + 2) % 3];

            NVector2 prev = default;
            bool havePrev = false;
            for (int s = 0; s <= RingSegments; s++)
            {
                float t = s / (float)RingSegments * Mathf.Tau;
                GVector3 p = origin + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * worldScale;
                if (!Project(camera, imageMin, p, out NVector2 sp))
                {
                    havePrev = false;
                    continue;
                }

                if (havePrev)
                {
                    drawList.AddLine(prev, sp, col, hot ? 3.5f : 2.5f);
                }

                prev = sp;
                havePrev = true;
            }
        }

        // While rotating, sweep a translucent pie slice and print the angle, Unreal-style.
        if (_dragKind == DragKind.Ring)
        {
            DrawRotationSweep(drawList, camera, imageMin, origin, worldScale, center);
        }
    }

    private void DrawRotationSweep(ImDrawListPtr drawList, Camera3D camera, NVector2 imageMin, GVector3 origin, float worldScale, NVector2 center)
    {
        GVector3 startVec = new Basis(_dragAxisWorld, -_ringAccum) * _ringLastVec;
        GVector3 normal = _dragAxisWorld;
        uint fill = ImGui.GetColorU32(HighlightColor with { W = 0.28f });

        int steps = Math.Max(1, (int)(Math.Abs(_ringAccum) / Mathf.Tau * RingSegments) + 1);
        NVector2 prev = center;
        bool havePrev = false;
        for (int s = 0; s <= steps; s++)
        {
            float t = _ringAccum * (s / (float)steps);
            GVector3 dir = new Basis(normal, t) * startVec;
            GVector3 p = origin + dir * worldScale;
            if (!Project(camera, imageMin, p, out NVector2 sp))
            {
                havePrev = false;
                continue;
            }

            if (havePrev)
            {
                drawList.AddTriangleFilled(center, prev, sp, fill);
            }

            prev = sp;
            havePrev = true;
        }

        string label = $"{Mathf.RadToDeg(_ringAccum):0.0}°";
        drawList.AddText(center + new NVector2(12.0f, -20.0f), ImGui.GetColorU32(new NVector4(1, 1, 1, 1)), label);
    }

    private NVector2[] PlaneQuad(Camera3D camera, NVector2 imageMin, GVector3 origin, Span<GVector3> axes, float worldScale, int normalAxis)
    {
        GVector3 u = axes[(normalAxis + 1) % 3];
        GVector3 v = axes[(normalAxis + 2) % 3];
        float lo = worldScale * 0.30f;
        float hi = worldScale * 0.62f;

        Span<GVector3> corners =
        [
            origin + u * lo + v * lo,
            origin + u * hi + v * lo,
            origin + u * hi + v * hi,
            origin + u * lo + v * hi,
        ];

        NVector2[] result = new NVector2[4];
        for (int c = 0; c < 4; c++)
        {
            Project(camera, imageMin, corners[c], out result[c]);
        }

        return result;
    }

    private void SortByDepth(Camera3D camera, GVector3 origin, Span<GVector3> axes, float worldScale, Span<int> order)
    {
        GVector3 cam = camera.GlobalPosition;
        Span<float> dist = [0, 0, 0];
        for (int i = 0; i < 3; i++)
        {
            dist[i] = (cam - (origin + axes[i] * worldScale)).LengthSquared();
        }

        // Simple descending insertion sort over three elements.
        for (int i = 1; i < 3; i++)
        {
            int key = order[i];
            int j = i - 1;
            while (j >= 0 && dist[order[j]] < dist[key])
            {
                order[j + 1] = order[j];
                j--;
            }

            order[j + 1] = key;
        }
    }

    // ---- Projection & geometry helpers ------------------------------------

    private static bool Project(Camera3D camera, NVector2 imageMin, GVector3 world, out NVector2 screen)
    {
        if (camera.IsPositionBehind(world))
        {
            screen = default;
            return false;
        }

        GVector2 p = camera.UnprojectPosition(world);
        screen = new NVector2(imageMin.X + p.X, imageMin.Y + p.Y);
        return true;
    }

    private static float WorldSizePerScreenPixel(Camera3D camera, GVector3 point, int viewportHeight)
    {
        // Perspective scale is governed by view-space depth (distance along the camera's
        // forward axis), not Euclidean distance. Using depth keeps the gizmo the same pixel
        // size no matter where the target sits on screen, instead of swelling near the edges.
        GVector3 forward = -camera.GlobalTransform.Basis.Z;
        float depth = Mathf.Max(0.001f, (point - camera.GlobalPosition).Dot(forward));
        float tanHalfFov = Mathf.Tan(Mathf.DegToRad(camera.Fov) * 0.5f);
        return 2.0f * depth * tanHalfFov / Math.Max(1, viewportHeight);
    }

    private static float DistanceToRing(Camera3D camera, NVector2 imageMin, GVector3 origin, Span<GVector3> axes, float worldScale, int axis, NVector2 mouse)
    {
        GVector3 u = axes[(axis + 1) % 3];
        GVector3 v = axes[(axis + 2) % 3];
        float best = float.MaxValue;
        NVector2 prev = default;
        bool havePrev = false;
        for (int s = 0; s <= RingSegments; s++)
        {
            float t = s / (float)RingSegments * Mathf.Tau;
            GVector3 p = origin + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * worldScale;
            if (!Project(camera, imageMin, p, out NVector2 sp))
            {
                havePrev = false;
                continue;
            }

            if (havePrev)
            {
                best = Math.Min(best, DistanceToSegment(mouse, prev, sp));
            }

            prev = sp;
            havePrev = true;
        }

        return best;
    }

    // Normal of the plane used to drag along an axis: it contains the axis and faces the
    // camera as much as possible, so the ray-plane intersection stays well-conditioned.
    private static GVector3 AxisDragPlaneNormal(GVector3 axisDir, GVector3 origin, Camera3D camera)
    {
        GVector3 viewDir = (origin - camera.GlobalPosition).Normalized();
        GVector3 normal = viewDir - axisDir * viewDir.Dot(axisDir);
        if (normal.LengthSquared() < 1e-8f)
        {
            // Camera is looking straight down the axis; any perpendicular will do.
            normal = axisDir.Cross(camera.GlobalTransform.Basis.Y);
            if (normal.LengthSquared() < 1e-8f)
            {
                normal = axisDir.Cross(camera.GlobalTransform.Basis.X);
            }
        }

        return normal.Normalized();
    }

    private static GVector3 RayPlane(GVector3 rayOrigin, GVector3 rayDir, GVector3 planePoint, GVector3 planeNormal)
    {
        float denom = rayDir.Dot(planeNormal);
        if (Mathf.Abs(denom) < 1e-6f)
        {
            return planePoint;
        }

        float t = (planePoint - rayOrigin).Dot(planeNormal) / denom;
        return rayOrigin + rayDir * t;
    }

    private static GVector3 InPlane(GVector3 v, GVector3 normal)
    {
        return v - normal * v.Dot(normal);
    }

    private static float SignedAngle(GVector3 a, GVector3 b, GVector3 axis)
    {
        float dot = Mathf.Clamp(a.Dot(b), -1.0f, 1.0f);
        float angle = Mathf.Acos(dot);
        return a.Cross(b).Dot(axis) < 0.0f ? -angle : angle;
    }

    private static NVector2 Normalize(NVector2 v)
    {
        float len = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
        return len < 1e-6f ? new NVector2(1.0f, 0.0f) : new NVector2(v.X / len, v.Y / len);
    }

    private static float DistanceToSegment(NVector2 p, NVector2 a, NVector2 b)
    {
        NVector2 ab = b - a;
        float lenSq = ab.X * ab.X + ab.Y * ab.Y;
        if (lenSq < 1e-6f)
        {
            return MathF.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        }

        float t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / lenSq, 0.0f, 1.0f);
        NVector2 proj = new(a.X + ab.X * t, a.Y + ab.Y * t);
        return MathF.Sqrt((p.X - proj.X) * (p.X - proj.X) + (p.Y - proj.Y) * (p.Y - proj.Y));
    }

    private static bool PointInQuad(NVector2 p, NVector2[] quad)
    {
        // Assumes a convex quad; the point is inside if it is on the same side of every edge.
        int sign = 0;
        for (int i = 0; i < 4; i++)
        {
            NVector2 a = quad[i];
            NVector2 b = quad[(i + 1) % 4];
            float cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            int s = cross > 0.0f ? 1 : cross < 0.0f ? -1 : 0;
            if (s == 0)
            {
                continue;
            }

            if (sign == 0)
            {
                sign = s;
            }
            else if (s != sign)
            {
                return false;
            }
        }

        return true;
    }
}
