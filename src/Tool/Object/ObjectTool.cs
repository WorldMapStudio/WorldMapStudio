using System.Collections.Generic;
using Godot;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// Blender-style object mode: select and deselect scene entities, and move or rotate them with the
/// gizmo or G/R modal transforms. Each completed move records one undoable command.
/// </summary>
public sealed class ObjectTool : ITool
{
    private readonly EditSessionManager _sessions;
    private readonly ObjectSelection _objectSelection;
    private readonly TransformGizmo _gizmo = new();
    private readonly ModalTransform _modalTransform = new();
    private bool _localSpacePreferred = true;

    // The gizmo drives this shared pivot; the resulting delta is applied to every selection.
    private Transform3D _pivot = Transform3D.Identity;
    private Transform3D _dragStartPivot;
    private readonly List<Transform3D> _dragStartTransforms = [];
    private SceneEntity[] _dragEntities = [];

    public ObjectTool(ToolContext context)
    {
        _sessions = context.Sessions;
        _objectSelection = new ObjectSelection(context.Selection, context.Scene);
        _gizmo.Axes = context.Axes;
        _modalTransform.Axes = context.Axes;
    }

    public string Name => "Object";

    public bool CapturesMouse => GizmoBusy || _objectSelection.IsDragging || _modalTransform.IsActive;

    // The gizmo only claims the mouse when something is selected; guarding on the selection
    // count keeps stale hover/use state from ever locking out fly and selection input.
    private bool GizmoBusy => _objectSelection.Selection.Count > 0 && (_gizmo.IsUsing || _gizmo.IsHovered);

    // Toolbar across the top of the viewport: gizmo mode and coordinate space.
    public void DrawToolbar()
    {
        if (ImGui.RadioButton("Move", _gizmo.Operation == GizmoOperation.Translate))
        {
            _gizmo.Operation = GizmoOperation.Translate;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Rotate", _gizmo.Operation == GizmoOperation.Rotate))
        {
            _gizmo.Operation = GizmoOperation.Rotate;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();

        // Local space is only meaningful for a single object; multi-selection forces world.
        bool localAvailable = _objectSelection.Selection.Count == 1;
        if (!localAvailable)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.RadioButton("Local", _localSpacePreferred))
        {
            _localSpacePreferred = true;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("World", !_localSpacePreferred))
        {
            _localSpacePreferred = false;
        }

        if (!localAvailable)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();
        ImGui.TextDisabled($"{_objectSelection.Selection.Count} selected");
    }

    public void UpdateViewport(in ViewportContext ctx)
    {
        // A running modal transform owns all input until it is confirmed or cancelled.
        if (_modalTransform.IsActive)
        {
            _objectSelection.DrawOutlines(ctx.Camera, ctx.ImageMin);
            _modalTransform.Update(_objectSelection, ctx.Camera, _localSpacePreferred, ctx.ImageMin, ctx.ImageSize);
            if (!_modalTransform.IsActive && _modalTransform.Confirmed)
            {
                RecordTransformEdit(_objectSelection.Selection, _modalTransform.StartTransforms);
            }

            return;
        }

        // W / E toggle between translate and rotate, mirroring the Unreal editor.
        if (ctx.Hovered && !ctx.CameraFlying)
        {
            if (Godot.Input.IsPhysicalKeyPressed(Key.W)) { _gizmo.Operation = GizmoOperation.Translate; }
            if (Godot.Input.IsPhysicalKeyPressed(Key.E)) { _gizmo.Operation = GizmoOperation.Rotate; }
        }

        // G / R begin a Blender-style modal grab / rotate on the current selection.
        if (ctx.Hovered && !ctx.CameraFlying && !GizmoBusy && !_objectSelection.IsDragging && _objectSelection.Selection.Count > 0)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.G, false)) { _modalTransform.Begin(ModalTransformMode.Translate, _objectSelection); return; }
            if (ImGui.IsKeyPressed(ImGuiKey.R, false)) { _modalTransform.Begin(ModalTransformMode.Rotate, _objectSelection); return; }
        }

        _objectSelection.DrawOutlines(ctx.Camera, ctx.ImageMin);
        DriveGizmo(ctx);

        bool canStartClick = ctx.Hovered && !ctx.CameraFlying && !GizmoBusy;
        _objectSelection.HandleInput(canStartClick, ctx.Camera, ctx.ImageMin, ctx.ImageSize);
    }

    // Places the gizmo at the centre of the selection and forwards its motion to every
    // selected object. Local space applies only when exactly one object is selected.
    private void DriveGizmo(in ViewportContext ctx)
    {
        IReadOnlyList<SceneEntity> selection = _objectSelection.Selection;
        if (selection.Count == 0)
        {
            return;
        }

        bool useLocal = _localSpacePreferred && selection.Count == 1;
        _gizmo.LocalSpace = useLocal;

        // While not dragging, keep the pivot pinned to the selection's centre.
        if (!_gizmo.IsUsing)
        {
            _pivot = _objectSelection.ComputePivot(useLocal);
        }

        bool wasUsing = _gizmo.IsUsing;
        Transform3D pivotBefore = _pivot;
        bool interactive = ctx.Hovered && !ctx.CameraFlying && !_objectSelection.IsDragging;
        _gizmo.Manipulate(ctx.Camera, ctx.ImageMin, ctx.ImageSize, interactive, ref _pivot);

        if (_gizmo.IsUsing && !wasUsing)
        {
            // Drag just started: snapshot the pivot and every object so we can apply the
            // total delta each frame (drift-free, unlike accumulating per-frame deltas).
            _dragStartPivot = pivotBefore;
            _dragEntities = [.. selection];
            _dragStartTransforms.Clear();
            foreach (SceneEntity obj in selection)
            {
                _dragStartTransforms.Add(obj.Transform);
            }
        }

        if (_gizmo.IsUsing)
        {
            Transform3D delta = _pivot * _dragStartPivot.AffineInverse();
            for (int i = 0; i < selection.Count; i++)
            {
                selection[i].Transform = delta * _dragStartTransforms[i];
            }
        }

        // Drag just ended: record the whole move as one undoable command.
        if (wasUsing && !_gizmo.IsUsing)
        {
            RecordTransformEdit(_dragEntities, _dragStartTransforms);
        }
    }

    // Records a finished move/rotate of the given entities from their captured start transforms to
    // their current ones as a single command, unless nothing actually moved.
    private void RecordTransformEdit(IReadOnlyList<SceneEntity> entities, IReadOnlyList<Transform3D> before)
    {
        int count = entities.Count;
        if (count == 0 || before.Count != count)
        {
            return;
        }

        var targets = new SceneEntity[count];
        var start = new Transform3D[count];
        var end = new Transform3D[count];
        bool changed = false;
        for (int i = 0; i < count; i++)
        {
            targets[i] = entities[i];
            start[i] = before[i];
            end[i] = entities[i].Transform;
            if (!start[i].IsEqualApprox(end[i]))
            {
                changed = true;
            }
        }

        if (changed)
        {
            _sessions.Record(new TransformEntitiesCommand(targets, start, end));
        }
    }
}
