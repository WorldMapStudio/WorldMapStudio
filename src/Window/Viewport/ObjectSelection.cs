using System;
using System.Collections.Generic;
using Godot;
using ImGuiNET;
using GVector2 = Godot.Vector2;
using GVector3 = Godot.Vector3;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// Tracks a viewport's selectable objects and the current selection, and implements
/// Unreal/Blender-style picking: left-click selects, shift-click adds or removes, and
/// dragging from empty space marquee-selects everything whose centre falls inside the box.
/// Selected objects get an outline, and the selection's centre (plus, for a single object,
/// its orientation) form the pivot that <see cref="TransformGizmo"/> and
/// <see cref="ModalTransform"/> operate around.
/// </summary>
public sealed class ObjectSelection
{
    private const float MarqueeThreshold = 5.0f;

    private readonly GVector3 _objectSize;

    public List<Node3D> Objects { get; } = [];
    public List<Node3D> Selection { get; } = [];

    /// <summary>True while a click or marquee drag is in progress.</summary>
    public bool IsDragging => _mouseDown;

    private bool _mouseDown;
    private bool _marquee;
    private NVector2 _marqueeStart;

    public ObjectSelection(GVector3 objectSize)
    {
        _objectSize = objectSize;
    }

    // canStartClick should already fold in "hovered, and no other system (fly camera,
    // gizmo) currently owns the mouse" -- once a drag is under way it continues regardless.
    public void HandleInput(bool canStartClick, Camera3D camera, NVector2 imageMin, NVector2 imageSize)
    {
        NVector2 mouse = ImGui.GetMousePos();

        if (!_mouseDown && canStartClick && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _mouseDown = true;
            _marquee = false;
            _marqueeStart = mouse;
        }

        if (!_mouseDown)
        {
            return;
        }

        // Track the button by its level, not the released edge. If a release event is ever
        // missed (e.g. it happens over another window or on a frame this window isn't drawn),
        // the next frame with the button up still finalises and clears the state, so the
        // viewport can never get stuck ignoring input.
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (!_marquee && (mouse - _marqueeStart).Length() > MarqueeThreshold)
            {
                _marquee = true;
            }

            if (_marquee)
            {
                DrawMarquee(_marqueeStart, mouse);
            }

            return;
        }

        _mouseDown = false;
        bool additive = Godot.Input.IsPhysicalKeyPressed(Key.Shift);
        if (_marquee)
        {
            ApplyBoxSelection(_marqueeStart, mouse, camera, imageMin, additive);
        }
        else
        {
            ApplyClickSelection(mouse, camera, imageMin, imageSize, additive);
        }

        _marquee = false;
    }

    private void ApplyClickSelection(NVector2 mouse, Camera3D camera, NVector2 imageMin, NVector2 imageSize, bool additive)
    {
        Node3D hit = Pick(mouse, camera, imageMin, imageSize);
        if (hit != null)
        {
            if (additive)
            {
                if (!Selection.Remove(hit))
                {
                    Selection.Add(hit);
                }
            }
            else
            {
                Selection.Clear();
                Selection.Add(hit);
            }
        }
        else if (!additive)
        {
            Selection.Clear();
        }
    }

    private void ApplyBoxSelection(NVector2 a, NVector2 b, Camera3D camera, NVector2 imageMin, bool additive)
    {
        NVector2 min = NVector2.Min(a, b);
        NVector2 max = NVector2.Max(a, b);
        if (!additive)
        {
            Selection.Clear();
        }

        foreach (Node3D obj in Objects)
        {
            if (!WorldToScreen(camera, obj.GlobalTransform.Origin, imageMin, out NVector2 screen))
            {
                continue;
            }

            bool inside = screen.X >= min.X && screen.X <= max.X && screen.Y >= min.Y && screen.Y <= max.Y;
            if (inside && !Selection.Contains(obj))
            {
                Selection.Add(obj);
            }
        }
    }

    // Nearest object under the cursor, or null. Uses an analytic ray-vs-box slab test.
    private Node3D Pick(NVector2 mouse, Camera3D camera, NVector2 imageMin, NVector2 imageSize)
    {
        GVector2 local = new(mouse.X - imageMin.X, mouse.Y - imageMin.Y);
        if (local.X < 0.0f || local.Y < 0.0f || local.X > imageSize.X || local.Y > imageSize.Y)
        {
            return null;
        }

        GVector3 from = camera.ProjectRayOrigin(local);
        GVector3 dir = camera.ProjectRayNormal(local);

        Node3D best = null;
        float bestT = float.PositiveInfinity;
        foreach (Node3D obj in Objects)
        {
            if (TryRayBox(from, dir, obj.GlobalTransform, _objectSize, out float t) && t < bestT)
            {
                bestT = t;
                best = obj;
            }
        }

        return best;
    }

    // Wireframe outline around each selected object, projected from its oriented box.
    public void DrawOutlines(Camera3D camera, NVector2 imageMin)
    {
        // 12 edges of a box as index pairs into the 8 corners below.
        ReadOnlySpan<int> edges =
        [
            0, 1, 1, 3, 3, 2, 2, 0, // bottom
            4, 5, 5, 7, 7, 6, 6, 4, // top
            0, 4, 1, 5, 2, 6, 3, 7, // verticals
        ];

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint color = ImGui.GetColorU32(new NVector4(1.0f, 0.62f, 0.20f, 0.95f));
        GVector3 half = _objectSize * 0.5f;
        Span<NVector2> corners = stackalloc NVector2[8];

        foreach (Node3D obj in Selection)
        {
            Transform3D xform = obj.GlobalTransform;
            bool allVisible = true;
            for (int c = 0; c < 8; c++)
            {
                GVector3 local = new((c & 1) == 0 ? -half.X : half.X,
                                     (c & 4) == 0 ? -half.Y : half.Y,
                                     (c & 2) == 0 ? -half.Z : half.Z);
                if (!WorldToScreen(camera, xform * local, imageMin, out corners[c]))
                {
                    allVisible = false;
                    break;
                }
            }

            if (!allVisible)
            {
                continue;
            }

            for (int e = 0; e < edges.Length; e += 2)
            {
                drawList.AddLine(corners[edges[e]], corners[edges[e + 1]], color, 1.5f);
            }
        }
    }

    // Centre of the selection and, for local space with exactly one object, its orientation.
    public Transform3D ComputePivot(bool useLocal)
    {
        GVector3 centre = GVector3.Zero;
        foreach (Node3D obj in Selection)
        {
            centre += obj.GlobalTransform.Origin;
        }

        centre /= Selection.Count;

        Basis basis = useLocal ? Selection[0].GlobalTransform.Basis.Orthonormalized() : Basis.Identity;
        return new Transform3D(basis, centre);
    }

    public static bool WorldToScreen(Camera3D camera, GVector3 world, NVector2 imageMin, out NVector2 screen)
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

    private static void DrawMarquee(NVector2 a, NVector2 b)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        NVector2 min = NVector2.Min(a, b);
        NVector2 max = NVector2.Max(a, b);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(new NVector4(0.30f, 0.55f, 0.95f, 0.20f)));
        drawList.AddRect(min, max, ImGui.GetColorU32(new NVector4(0.40f, 0.65f, 1.0f, 0.90f)));
    }

    // Slab test: transform the ray into the box's local space and clip against its extents.
    // Reports the entry distance so the caller can pick the nearest hit among several boxes.
    private static bool TryRayBox(GVector3 origin, GVector3 dir, Transform3D boxTransform, GVector3 size, out float tHit)
    {
        tHit = 0.0f;
        Transform3D inv = boxTransform.AffineInverse();
        GVector3 o = inv * origin;
        GVector3 d = inv.Basis * dir;
        GVector3 half = size * 0.5f;

        float[] oc = [o.X, o.Y, o.Z];
        float[] dc = [d.X, d.Y, d.Z];
        float[] hc = [half.X, half.Y, half.Z];

        float tMin = float.NegativeInfinity;
        float tMax = float.PositiveInfinity;
        for (int a = 0; a < 3; a++)
        {
            if (Mathf.Abs(dc[a]) < 1e-8f)
            {
                if (oc[a] < -hc[a] || oc[a] > hc[a])
                {
                    return false;
                }

                continue;
            }

            float t1 = (-hc[a] - oc[a]) / dc[a];
            float t2 = (hc[a] - oc[a]) / dc[a];
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            if (tMin > tMax)
            {
                return false;
            }
        }

        if (tMax < 0.0f)
        {
            return false;
        }

        tHit = tMin >= 0.0f ? tMin : tMax;
        return true;
    }
}
