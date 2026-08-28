using System.Collections.Generic;
using System.Globalization;
using Godot;
using ImGuiNET;
using GVector2 = Godot.Vector2;
using GVector3 = Godot.Vector3;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

public enum ModalTransformMode
{
    None,
    Translate,
    Rotate,
    Scale,
}

/// <summary>
/// Blender-style modal transform: press G to grab, R to rotate, or S to scale the current selection,
/// then optionally constrain to an axis (X/Y/Z; pressing the same one again cycles
/// global/local/free) and/or type an exact numeric value. Confirm with left-click or
/// Enter, cancel with right-click or Escape. Operates on a shared pivot, so a
/// multi-object selection transforms together, exactly like <see cref="TransformGizmo"/>.
/// </summary>
public sealed class ModalTransform
{
    private static readonly NVector4[] AxisColors =
    [
        new(0.91f, 0.24f, 0.28f, 1.0f), // X
        new(0.49f, 0.78f, 0.16f, 1.0f), // Y
        new(0.22f, 0.49f, 0.93f, 1.0f), // Z
    ];

    public ModalTransformMode Mode { get; private set; } = ModalTransformMode.None;
    public bool IsActive => Mode != ModalTransformMode.None;

    /// <summary>Whether the last completed transform was confirmed (true) or cancelled (false).</summary>
    public bool Confirmed { get; private set; }

    /// <summary>The entities being transformed, captured at <see cref="Begin"/>.</summary>
    public IReadOnlyList<SceneEntity> Targets => _targets;

    /// <summary>Their transforms at <see cref="Begin"/>, aligned with <see cref="Targets"/>.</summary>
    public IReadOnlyList<Transform3D> StartTransforms => _startTransforms;

    /// <summary>
    /// The user's coordinate system. The X/Y/Z axis constraints and typed values are all in user
    /// space; this maps them onto the Godot directions actually moved. Defaults to Godot's axes.
    /// </summary>
    public AxisConvention Axes { get; set; } = AxisConvention.GodotDefault;

    private int _axis = -1; // -1 = free, 0/1/2 = X/Y/Z
    private bool _axisLocal;
    private bool _axisExclude; // true = constrained to the plane of the OTHER two axes (Blender's Shift+axis)
    private string _numeric = string.Empty;
    private NVector2 _startMouse;
    private Transform3D _startPivot;
    private readonly List<Transform3D> _startTransforms = [];

    // Captured up front rather than re-read each frame: the selection can change under a running
    // modal, and an index mismatch against the start transforms would fling entities across the map.
    private readonly List<SceneEntity> _targets = [];

    public void Begin(ModalTransformMode mode, ObjectSelection selection)
    {
        Mode = mode;
        Confirmed = false;
        _axis = -1;
        _axisLocal = false;
        _axisExclude = false;
        _numeric = string.Empty;
        _startMouse = ImGui.GetMousePos();
        _startPivot = selection.ComputePivot(false);
        _startTransforms.Clear();
        _targets.Clear();
        _targets.AddRange(selection.Movable);
        foreach (SceneEntity obj in _targets)
        {
            _startTransforms.Add(obj.Transform);
        }
    }

    /// <summary>
    /// Advance the modal by one frame: reads axis/number keys, applies the live preview to
    /// every selected object, and draws guides plus the HUD. On confirm or cancel, restores
    /// (if cancelled) and sets <see cref="Mode"/> back to <see cref="ModalTransformMode.None"/>.
    /// </summary>
    public void Update(ObjectSelection selection, Camera3D camera, bool localSpacePreferred, NVector2 imageMin, NVector2 imageSize)
    {
        HandleAxisKey(ImGuiKey.X, 0, selection, localSpacePreferred);
        HandleAxisKey(ImGuiKey.Y, 1, selection, localSpacePreferred);
        HandleAxisKey(ImGuiKey.Z, 2, selection, localSpacePreferred);
        HandleNumericKeys();

        // Live preview: apply the current delta to every selected object.
        Transform3D delta = ComputeDelta(camera, ImGui.GetMousePos(), imageMin);
        for (int i = 0; i < _targets.Count; i++)
        {
            _targets[i].Transform = Mode == ModalTransformMode.Scale
                ? ScaleObjectTransform(delta, _startTransforms[i])
                : delta * _startTransforms[i];
        }

        DrawGuides(camera, imageMin);
        DrawHud(imageMin);

        bool confirm = ImGui.IsKeyPressed(ImGuiKey.Enter, false) ||
                       ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false) ||
                       ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        bool cancel = ImGui.IsKeyPressed(ImGuiKey.Escape, false) ||
                      ImGui.IsMouseClicked(ImGuiMouseButton.Right);

        if (cancel)
        {
            for (int i = 0; i < _targets.Count; i++)
            {
                _targets[i].Transform = _startTransforms[i];
            }

            Confirmed = false;
            Mode = ModalTransformMode.None;
        }
        else if (confirm)
        {
            Confirmed = true;
            Mode = ModalTransformMode.None;
        }
    }

    // X/Y/Z pick an axis; pressing the same one again cycles to the other space, then to
    // free. The first press honours the toolbar's Local/World choice (local needs one object).
    // Holding Shift while pressing (translate only) constrains to the plane of the OTHER two
    // axes instead -- e.g. Shift+X moves freely in Y/Z -- exactly like Blender's grab tool.
    private void HandleAxisKey(ImGuiKey key, int axis, ObjectSelection selection, bool localSpacePreferred)
    {
        if (!ImGui.IsKeyPressed(key, false))
        {
            return;
        }

        bool canLocal = _targets.Count == 1;
        bool preferLocal = localSpacePreferred && canLocal;
        bool exclude = Mode == ModalTransformMode.Translate && Godot.Input.IsPhysicalKeyPressed(Key.Shift);

        if (_axis != axis || _axisExclude != exclude)
        {
            // A different axis, or switching between "along this axis" and "excluding this
            // axis", starts a fresh cycle at the preferred space.
            _axis = axis;
            _axisExclude = exclude;
            _axisLocal = preferLocal;
        }
        else if (preferLocal)
        {
            if (_axisLocal) { _axisLocal = false; }   // local -> global
            else { _axis = -1; _axisExclude = false; } // global -> free
        }
        else if (!_axisLocal && canLocal)
        {
            _axisLocal = true;                        // global -> local
        }
        else
        {
            _axis = -1;                               // -> free
            _axisLocal = false;
            _axisExclude = false;
        }
    }

    private void HandleNumericKeys()
    {
        for (int d = 0; d <= 9; d++)
        {
            if (ImGui.IsKeyPressed(ImGuiKey._0 + d, false) || ImGui.IsKeyPressed(ImGuiKey.Keypad0 + d, false))
            {
                _numeric += (char)('0' + d);
            }
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Period, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadDecimal, false))
        {
            if (!_numeric.Contains('.'))
            {
                _numeric += _numeric.Length == 0 ? "0." : ".";
            }
        }

        // Minus toggles the sign, like Blender.
        if (ImGui.IsKeyPressed(ImGuiKey.Minus, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadSubtract, false))
        {
            _numeric = _numeric.StartsWith('-') ? _numeric[1..] : "-" + _numeric;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Backspace, false) && _numeric.Length > 0)
        {
            _numeric = _numeric[..^1];
        }
    }

    private bool TryNumeric(out float value)
    {
        // Treat a bare sign or dot as "typing in progress" worth 0, so the preview reacts.
        if (_numeric.Length == 0 || _numeric == "-")
        {
            value = 0.0f;
            return _numeric.Length > 0;
        }

        return float.TryParse(_numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || SetZero(out value);

        static bool SetZero(out float v)
        {
            v = 0.0f;
            return true;
        }
    }

    // The user's constraint axis in Godot space: the convention's mapped direction, carried into
    // the target's own frame for a local-space constraint (matching the gizmo, and reducing to the
    // object's basis column under the default identity convention).
    private GVector3 AxisVec(int axis)
    {
        GVector3 userAxis = Axes.UserAxis(axis);
        if (_axisLocal && _startTransforms.Count == 1)
        {
            return (_startTransforms[0].Basis * userAxis).Normalized();
        }

        return userAxis;
    }

    private Transform3D ComputeDelta(Camera3D camera, NVector2 mouse, NVector2 imageMin)
    {
        GVector3 pivot = _startPivot.Origin;
        bool numeric = TryNumeric(out float number);

        if (Mode == ModalTransformMode.Translate)
        {
            GVector3 move;
            if (numeric)
            {
                move = AxisVec(_axis < 0 ? 0 : _axis) * number;
            }
            else if (_axis < 0)
            {
                // Free move: slide across a plane facing the camera through the pivot.
                GVector3 normal = -camera.GlobalTransform.Basis.Z;
                move = RayToPlane(camera, mouse, imageMin, pivot, normal) - RayToPlane(camera, _startMouse, imageMin, pivot, normal);
            }
            else if (_axisExclude)
            {
                // Shift+axis: free movement in the plane the excluded axis is normal to.
                GVector3 normal = AxisVec(_axis);
                move = RayToPlane(camera, mouse, imageMin, pivot, normal) - RayToPlane(camera, _startMouse, imageMin, pivot, normal);
            }
            else
            {
                GVector3 axis = AxisVec(_axis);
                GVector3 normal = AxisDragPlaneNormal(axis, pivot, camera);
                float now = (RayToPlane(camera, mouse, imageMin, pivot, normal) - pivot).Dot(axis);
                float start = (RayToPlane(camera, _startMouse, imageMin, pivot, normal) - pivot).Dot(axis);
                move = axis * (now - start);
            }

            return new Transform3D(Basis.Identity, move);
        }

        if (Mode == ModalTransformMode.Scale)
        {
            float factor = numeric ? number : ScaleFactorFromScreen(camera, imageMin, pivot, _startMouse, mouse);
            factor = Mathf.Max(0.01f, factor);
            return _axis < 0
                ? ScaleAround(pivot, factor)
                : ScaleAround(pivot, AxisVec(_axis), factor);
        }

        // Rotate.
        GVector3 rotAxis = _axis < 0 ? (camera.GlobalPosition - pivot).Normalized() : AxisVec(_axis);
        float angle;
        if (numeric)
        {
            angle = Mathf.DegToRad(number);
        }
        else
        {
            // Angle the mouse sweeps around the pivot on screen; flip so the object turns the
            // same way the cursor does regardless of which side of the axis faces the camera.
            ObjectSelection.WorldToScreen(camera, pivot, imageMin, out NVector2 centre);
            float now = Mathf.Atan2(mouse.Y - centre.Y, mouse.X - centre.X);
            float start = Mathf.Atan2(_startMouse.Y - centre.Y, _startMouse.X - centre.X);
            float facing = rotAxis.Dot(camera.GlobalPosition - pivot);
            angle = -(now - start) * (facing >= 0.0f ? 1.0f : -1.0f);
        }

        Basis rotation = new(rotAxis, angle);
        return new Transform3D(rotation, pivot - rotation * pivot);
    }

    // Draws the constrained axis as a colored line through the pivot, or a ring at the pivot
    // for a free (view-axis) rotation, so it's clear what the transform acts on.
    private void DrawGuides(Camera3D camera, NVector2 imageMin)
    {
        if (!ObjectSelection.WorldToScreen(camera, _startPivot.Origin, imageMin, out NVector2 centre))
        {
            return;
        }

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();

        if (_axis >= 0 && _axisExclude)
        {
            // Shift+axis: draw both of the OTHER two axes through the pivot, so the excluded
            // one reads clearly as "not this axis" -- the plane they span is what moves.
            DrawAxisLine(drawList, camera, imageMin, centre, (_axis + 1) % 3);
            DrawAxisLine(drawList, camera, imageMin, centre, (_axis + 2) % 3);
        }
        else if (_axis >= 0)
        {
            DrawAxisLine(drawList, camera, imageMin, centre, _axis);
        }
        else if (Mode == ModalTransformMode.Rotate)
        {
            drawList.AddCircle(centre, 64.0f, ImGui.GetColorU32(new NVector4(0.9f, 0.9f, 0.9f, 0.5f)), 48, 1.5f);
        }
    }

    // Draws one constrained axis as a colored line through the pivot, extended far enough
    // off-screen in both directions to read as infinite.
    private void DrawAxisLine(ImDrawListPtr drawList, Camera3D camera, NVector2 imageMin, NVector2 centre, int axisIndex)
    {
        GVector3 axis = AxisVec(axisIndex);
        // Screen direction of the axis, from the pivot toward a point one unit along it.
        bool ok = ObjectSelection.WorldToScreen(camera, _startPivot.Origin + axis, imageMin, out NVector2 ahead) ||
                  ObjectSelection.WorldToScreen(camera, _startPivot.Origin - axis, imageMin, out ahead);
        if (!ok)
        {
            return;
        }

        NVector2 dir = ahead - centre;
        float len = dir.Length();
        if (len <= 1e-3f)
        {
            return;
        }

        dir /= len;
        uint col = ImGui.GetColorU32(AxisColors[axisIndex]);
        drawList.AddLine(centre - dir * 4000.0f, centre + dir * 4000.0f, col, 1.5f);
    }

    private void DrawHud(NVector2 imageMin)
    {
        string op = Mode switch
        {
            ModalTransformMode.Translate => "Move",
            ModalTransformMode.Rotate => "Rotate",
            ModalTransformMode.Scale => "Scale",
            _ => "Transform",
        };
        string axis = string.Empty;
        if (_axis >= 0)
        {
            string axisLabel = _axisExclude ? "XYZ".Remove(_axis, 1) : "XYZ".Substring(_axis, 1);
            axis = $" {(_axisLocal ? "local " : string.Empty)}{axisLabel}";
        }
        string value = _numeric.Length > 0
            ? $": {_numeric}{(Mode == ModalTransformMode.Rotate ? "°" : string.Empty)}"
            : string.Empty;
        string hint = "   (LMB/Enter confirm, RMB/Esc cancel, X/Y/Z axis, Shift+axis to exclude, type a value)";

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.AddText(imageMin + new NVector2(8.0f, 6.0f),
            ImGui.GetColorU32(new NVector4(1.0f, 0.85f, 0.35f, 1.0f)), $"{op}{axis}{value}{hint}");
    }

    // Intersect the cursor ray with a plane, in the viewport's local pixel space.
    private static GVector3 RayToPlane(Camera3D camera, NVector2 mouse, NVector2 imageMin, GVector3 planePoint, GVector3 planeNormal)
    {
        GVector2 local = new(mouse.X - imageMin.X, mouse.Y - imageMin.Y);
        GVector3 origin = camera.ProjectRayOrigin(local);
        GVector3 dir = camera.ProjectRayNormal(local);
        float denom = dir.Dot(planeNormal);
        if (Mathf.Abs(denom) < 1e-6f)
        {
            return planePoint;
        }

        return origin + dir * ((planePoint - origin).Dot(planeNormal) / denom);
    }

    private static Transform3D ScaleObjectTransform(Transform3D delta, Transform3D start)
    {
        start.Origin = delta * start.Origin;
        return start;
    }

    private static Transform3D ScaleAround(GVector3 pivot, float factor)
    {
        Basis basis = Basis.Identity;
        basis.X *= factor;
        basis.Y *= factor;
        basis.Z *= factor;
        return new Transform3D(basis, pivot - basis * pivot);
    }

    private static Transform3D ScaleAround(GVector3 pivot, GVector3 axis, float factor)
    {
        Basis basis = Basis.Identity;
        basis.X = ScaleVector(basis.X, axis, factor);
        basis.Y = ScaleVector(basis.Y, axis, factor);
        basis.Z = ScaleVector(basis.Z, axis, factor);
        return new Transform3D(basis, pivot - basis * pivot);
    }

    private static GVector3 ScaleVector(GVector3 value, GVector3 axis, float factor) =>
        value + axis * (value.Dot(axis) * (factor - 1.0f));

    private static float ScaleFactorFromScreen(Camera3D camera, NVector2 imageMin, GVector3 pivot, NVector2 startMouse, NVector2 mouse)
    {
        if (!ObjectSelection.WorldToScreen(camera, pivot, imageMin, out NVector2 centre))
        {
            return Mathf.Exp((startMouse.Y - mouse.Y) / 120.0f);
        }

        float startDistance = Mathf.Max(8.0f, (startMouse - centre).Length());
        float currentDistance = (mouse - centre).Length();
        return currentDistance / startDistance;
    }

    // Plane containing the axis and facing the camera (mirrors the gizmo's stable axis drag).
    private static GVector3 AxisDragPlaneNormal(GVector3 axisDir, GVector3 origin, Camera3D camera)
    {
        GVector3 viewDir = (origin - camera.GlobalPosition).Normalized();
        GVector3 normal = viewDir - axisDir * viewDir.Dot(axisDir);
        if (normal.LengthSquared() < 1e-8f)
        {
            normal = axisDir.Cross(camera.GlobalTransform.Basis.Y);
            if (normal.LengthSquared() < 1e-8f)
            {
                normal = axisDir.Cross(camera.GlobalTransform.Basis.X);
            }
        }

        return normal.Normalized();
    }
}
