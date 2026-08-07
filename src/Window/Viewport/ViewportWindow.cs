using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ImGuiNET;
using GVector2 = Godot.Vector2;
using GVector3 = Godot.Vector3;
using NVector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// An ImGui window that renders a Godot 3D <see cref="SubViewport"/> with an infinite,
/// camera-following grid, similar to an empty Blender scene. It owns the camera, the
/// <see cref="FlyCamera"/> navigation and the demo scene, and each frame hands viewport
/// interaction to the active <see cref="ITool"/> from the shared <see cref="ToolSystem"/>.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class ViewportWindow : Window
{
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
    private readonly SceneEntityRegistry _scene;
    private readonly ToolSystem _tools;
    private readonly StreamingSystem _streaming;
    private readonly HashSet<SceneEntity> _represented = [];

    public ViewportWindow(WindowManager manager) : base("Viewport", defaultSize: new NVector2(720, 480))
    {
        EditorContext context = manager.Context;
        Node owner = context.Root;
        _flyCamera = new FlyCamera(owner, new GVector3(8.0f, 6.0f, 8.0f));
        _scene = context.Scene;
        _tools = context.Tools;
        _streaming = context.Streaming;
        _axes = context.Axes;

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
        _scene.Add(new DemoBoxEntity(position, color, yaw));
    }

    // Creates a viewport representation for every loaded entity that lacks one, and tears down the
    // representation of any entity that has left the registry (e.g. an undone creation).
    private void SyncRepresentations()
    {
        foreach (SceneEntity entity in _scene.Entities)
        {
            if (_represented.Add(entity))
            {
                entity.CreateRepresentation(_viewport);
            }
        }

        _represented.RemoveWhere(entity =>
        {
            if (_scene.Entities.Contains(entity))
            {
                return false;
            }

            entity.DestroyRepresentation();
            return true;
        });
    }

    protected override ImGuiWindowFlags Flags => ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    protected override void DrawContent()
    {
        _streaming.Update(_flyCamera.Position);
        SyncRepresentations();

        ITool? tool = _tools.Active;
        tool?.DrawToolbar();

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

        // The active tool owns the mouse buttons while interacting, so the fly camera
        // (right mouse) only starts when the tool isn't capturing.
        _flyCamera.Update(hovered && !(tool?.CapturesMouse ?? false));
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
        tool?.UpdateViewport(new ViewportContext(_camera, imageMin, imageSize, hovered, _flyCamera.IsFlying));
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
}
