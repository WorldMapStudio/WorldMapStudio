using System;
using System.Collections.Generic;
using Godot;
using ImGuiNET;
using GVector2 = Godot.Vector2;
using GVector3 = Godot.Vector3;
using NVector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// An ImGui window that renders a Godot 3D <see cref="SubViewport"/> containing an
/// infinite, camera-following grid, similar to an empty Blender scene.
///
/// Combines a few independent systems: <see cref="FlyCamera"/> for Unreal-style
/// navigation, <see cref="ObjectSelection"/> for click/shift-click/marquee picking,
/// <see cref="TransformGizmo"/> for on-screen dragging, and <see cref="ModalTransform"/>
/// for Blender-style G/R keyboard transforms. This class just wires them together and
/// owns the demo scene they operate on.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class ViewportWindow : Window
{
    private static readonly GVector3 DemoObjectSize = new(2.0f, 2.0f, 2.0f);

    // Dim red/green/blue for the grid's axis lines, indexed by *user* axis (0 = X, 1 = Y, 2 = Z)
    // so the line for whichever Godot axis a user axis is mapped onto always reads as that color.
    private static readonly Color[] UserAxisLineColors =
    [
        new Color(0.55f, 0.18f, 0.18f), // user X - red
        new Color(0.18f, 0.5f, 0.18f),  // user Y - green
        new Color(0.18f, 0.32f, 0.6f),  // user Z - blue
    ];

    private readonly SubViewport _viewport;
    private readonly Camera3D _camera;
    private readonly MeshInstance3D _grid;
    private readonly MeshInstance3D _upAxisLine;
    private readonly AxisConvention _axes;

    private readonly FlyCamera _flyCamera;
    private readonly ObjectSelection _objectSelection;
    private readonly TransformGizmo _gizmo = new();
    private readonly ModalTransform _modalTransform = new();
    private bool _localSpacePreferred = true;

    // The gizmo drives this shared pivot; the resulting delta is applied to every selection.
    private Transform3D _pivot = Transform3D.Identity;
    private Transform3D _dragStartPivot;
    private readonly List<Transform3D> _dragStartTransforms = [];

    public ViewportWindow(WindowManager manager) : base("Viewport", defaultSize: new NVector2(720, 480))
    {
        Node owner = manager.Root;
        _flyCamera = new FlyCamera(owner, new GVector3(8.0f, 6.0f, 8.0f));
        _objectSelection = new ObjectSelection(DemoObjectSize);
        _axes = manager.Axes;

        // Route the gizmo and modal transform through the project's coordinate system, so the
        // user's X/Y/Z always mean the axes they chose, remapped onto Godot's internal axes.
        _gizmo.Axes = _axes;
        _modalTransform.Axes = _axes;

        _viewport = new SubViewport
        {
            Name = "ViewportWindowSubViewport",
            Size = new Vector2I(720, 480),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            OwnWorld3D = true,
        };

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.28f, 0.28f, 0.28f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.28f, 0.28f, 0.28f),
        };

        _camera = new Camera3D
        {
            Name = "Camera",
            Current = true,
            Near = 0.05f,
            Far = 5000.0f,
            Environment = environment,
        };

        var gridShader = GD.Load<Shader>("res://src/Window/Viewport/InfiniteGrid.gdshader");
        _grid = new MeshInstance3D
        {
            Name = "Grid",
            Mesh = new PlaneMesh { Size = new GVector2(4000.0f, 4000.0f) },
            MaterialOverride = new ShaderMaterial { Shader = gridShader },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        // Completes the grid's X/Z axis lines with a vertical Y line through the origin, drawn
        // on a quad kept facing the camera each frame (see DrawContent) so it reads as a crisp
        // line from any angle, mirroring the grid's own axis line rendering.
        var axisLineShader = GD.Load<Shader>("res://src/Window/Viewport/AxisLine.gdshader");
        _upAxisLine = new MeshInstance3D
        {
            Name = "UpAxisLine",
            Mesh = new QuadMesh { Size = new GVector2(4000.0f, 4000.0f) },
            MaterialOverride = new ShaderMaterial { Shader = axisLineShader },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        _viewport.AddChild(_camera);
        _viewport.AddChild(_grid);
        _viewport.AddChild(_upAxisLine);
        CreateDemoScene();
        owner.AddChild(_viewport);

        _flyCamera.LookAt(GVector3.Zero);
        _flyCamera.ApplyTo(_camera);
    }

    // A handful of lit boxes scattered on the grid, some rotated so local vs. world space
    // is visible. Picking is analytic (ray vs. box), so no physics body is needed.
    private void CreateDemoScene()
    {
        AddBox(new GVector3(-6.0f, 1.0f, -2.0f), new Color(0.85f, 0.55f, 0.28f), 0.0f);
        AddBox(new GVector3(0.0f, 1.0f, 0.0f), new Color(0.45f, 0.70f, 0.85f), 0.6f);
        AddBox(new GVector3(5.0f, 1.0f, 2.0f), new Color(0.60f, 0.80f, 0.45f), -0.3f);
        AddBox(new GVector3(3.0f, 1.0f, -5.0f), new Color(0.82f, 0.45f, 0.62f), 1.1f);
        AddBox(new GVector3(-3.0f, 1.0f, 5.0f), new Color(0.80f, 0.75f, 0.35f), 0.0f);
    }

    private void AddBox(GVector3 position, Color color, float yaw)
    {
        var box = new Node3D
        {
            Name = $"Box{_objectSelection.Objects.Count}",
            Transform = new Transform3D(new Basis(GVector3.Up, yaw), position),
        };

        box.AddChild(new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new BoxMesh { Size = DemoObjectSize },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                Metallic = 0.2f,
                Roughness = 0.55f,
            },
        });

        _viewport.AddChild(box);
        _objectSelection.Objects.Add(box);
    }

    protected override ImGuiWindowFlags Flags => ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    protected override void DrawContent()
    {
        DrawToolbar();

        NVector2 region = ImGui.GetContentRegionAvail();
        int width = Math.Max(1, (int)region.X);
        int height = Math.Max(1, (int)region.Y);
        if (_viewport.Size.X != width || _viewport.Size.Y != height)
        {
            _viewport.Size = new Vector2I(width, height);
        }

        IntPtr textureId = (IntPtr)_viewport.GetTexture().GetRid().Id;
        ImGui.Image(textureId, new NVector2(width, height));

        bool hovered = ImGui.IsItemHovered();
        NVector2 imageMin = ImGui.GetItemRectMin();
        NVector2 imageSize = new(width, height);

        // The gizmo, marquee and modal transform own the mouse buttons, so the fly camera
        // (right mouse) only starts when none of them is in play.
        _flyCamera.Update(hovered && !GizmoBusy && !_objectSelection.IsDragging && !_modalTransform.IsActive);
        _flyCamera.ApplyTo(_camera);

        // Keep the grid plane centred under the camera so the grid feels endless.
        _grid.GlobalPosition = new GVector3(_flyCamera.Position.X, 0.0f, _flyCamera.Position.Z);

        // The up axis line stays pinned to the true X=0/Z=0 column (that's the line it draws),
        // only following the camera vertically, and is kept yawed to face the camera so its
        // screen-space line rendering stays correct from any angle.
        _upAxisLine.GlobalPosition = new GVector3(0.0f, _flyCamera.Position.Y, 0.0f);
        GVector3 lookTarget = new(_camera.GlobalPosition.X, _upAxisLine.GlobalPosition.Y, _camera.GlobalPosition.Z);
        if ((lookTarget - _upAxisLine.GlobalPosition).LengthSquared() > 1e-6f)
        {
            _upAxisLine.LookAt(lookTarget, GVector3.Up);
        }

        UpdateAxisLineColors();
        UpdateSelectionAndGizmo(hovered, imageMin, imageSize);
    }

    // Colors the grid's two horizontal axis lines and the vertical up line by whichever user
    // axis maps onto that Godot spatial axis under the project's AxisConvention, so the lines
    // always read as the user's own X/Y/Z (red/green/blue) no matter how they are remapped.
    private void UpdateAxisLineColors()
    {
        var gridMaterial = (ShaderMaterial)_grid.MaterialOverride;
        gridMaterial.SetShaderParameter("x_axis_color", ColorForSpatialAxis(0));
        gridMaterial.SetShaderParameter("z_axis_color", ColorForSpatialAxis(2));

        var upLineMaterial = (ShaderMaterial)_upAxisLine.MaterialOverride;
        upLineMaterial.SetShaderParameter("axis_color", ColorForSpatialAxis(1));
    }

    private Color ColorForSpatialAxis(int spatial)
    {
        for (int userAxis = 0; userAxis < 3; userAxis++)
        {
            if (_axes.Get(userAxis).Spatial() == spatial)
            {
                return UserAxisLineColors[userAxis];
            }
        }

        return Colors.White; // Unreachable: the convention is always a full permutation.
    }

    // Toolbar across the top of the viewport: gizmo mode and coordinate space.
    private void DrawToolbar()
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

    // The gizmo only claims the mouse when something is selected; guarding on the selection
    // count keeps stale hover/use state from ever locking out fly and selection input.
    private bool GizmoBusy => _objectSelection.Selection.Count > 0 && (_gizmo.IsUsing || _gizmo.IsHovered);

    private void UpdateSelectionAndGizmo(bool hovered, NVector2 imageMin, NVector2 imageSize)
    {
        // A running modal transform owns all input until it is confirmed or cancelled.
        if (_modalTransform.IsActive)
        {
            _objectSelection.DrawOutlines(_camera, imageMin);
            _modalTransform.Update(_objectSelection, _camera, _localSpacePreferred, imageMin, imageSize);
            return;
        }

        // W / E toggle between translate and rotate, mirroring the Unreal editor.
        if (hovered && !_flyCamera.IsFlying)
        {
            if (Godot.Input.IsPhysicalKeyPressed(Key.W)) { _gizmo.Operation = GizmoOperation.Translate; }
            if (Godot.Input.IsPhysicalKeyPressed(Key.E)) { _gizmo.Operation = GizmoOperation.Rotate; }
        }

        // G / R begin a Blender-style modal grab / rotate on the current selection.
        if (hovered && !_flyCamera.IsFlying && !GizmoBusy && !_objectSelection.IsDragging && _objectSelection.Selection.Count > 0)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.G, false)) { _modalTransform.Begin(ModalTransformMode.Translate, _objectSelection); return; }
            if (ImGui.IsKeyPressed(ImGuiKey.R, false)) { _modalTransform.Begin(ModalTransformMode.Rotate, _objectSelection); return; }
        }

        _objectSelection.DrawOutlines(_camera, imageMin);
        DriveGizmo(hovered, imageMin, imageSize);

        bool canStartClick = hovered && !_flyCamera.IsFlying && !GizmoBusy;
        _objectSelection.HandleInput(canStartClick, _camera, imageMin, imageSize);
    }

    // Places the gizmo at the centre of the selection and forwards its motion to every
    // selected object. Local space applies only when exactly one object is selected.
    private void DriveGizmo(bool hovered, NVector2 imageMin, NVector2 imageSize)
    {
        List<Node3D> selection = _objectSelection.Selection;
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
        bool interactive = hovered && !_flyCamera.IsFlying && !_objectSelection.IsDragging;
        _gizmo.Manipulate(_camera, imageMin, imageSize, interactive, ref _pivot);

        if (_gizmo.IsUsing && !wasUsing)
        {
            // Drag just started: snapshot the pivot and every object so we can apply the
            // total delta each frame (drift-free, unlike accumulating per-frame deltas).
            _dragStartPivot = pivotBefore;
            _dragStartTransforms.Clear();
            foreach (Node3D obj in selection)
            {
                _dragStartTransforms.Add(obj.GlobalTransform);
            }
        }

        if (_gizmo.IsUsing)
        {
            Transform3D delta = _pivot * _dragStartPivot.AffineInverse();
            for (int i = 0; i < selection.Count; i++)
            {
                selection[i].GlobalTransform = delta * _dragStartTransforms[i];
            }
        }
    }
}
