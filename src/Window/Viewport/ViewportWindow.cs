using System;
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
/// Navigation mirrors the Unreal editor viewport: hold the right mouse button to fly.
/// While held, moving the mouse looks around, W/A/S/D moves, E/Q rises and falls,
/// Shift boosts, and the wheel changes fly speed. The cursor is hidden and pinned in
/// place for the duration, so it never physically moves or snags on other UI.
/// </summary>
public sealed class ViewportWindow : ImGuiWindow
{
    private const float LookSensitivity = 0.0025f;
    private const float MinPitch = -1.55f;
    private const float MaxPitch = 1.55f;
    private const float MinFlySpeed = 0.5f;
    private const float MaxFlySpeed = 200.0f;
    private const float BoostMultiplier = 4.0f;

    private readonly Node _owner;
    private readonly SubViewport _viewport;
    private readonly Camera3D _camera;
    private readonly MeshInstance3D _grid;

    private GVector3 _position = new(8.0f, 6.0f, 8.0f);
    private float _yaw;
    private float _pitch;
    private float _flySpeed = 8.0f;
    private bool _flying;
    private Vector2I _flyAnchor;

    public ViewportWindow(Node owner) : base("Viewport", defaultSize: new NVector2(720, 480))
    {
        _owner = owner;

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

        var shader = GD.Load<Shader>("res://src/Window/Viewport/InfiniteGrid.gdshader");
        _grid = new MeshInstance3D
        {
            Name = "Grid",
            Mesh = new PlaneMesh { Size = new GVector2(4000.0f, 4000.0f) },
            MaterialOverride = new ShaderMaterial { Shader = shader },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        _viewport.AddChild(_camera);
        _viewport.AddChild(_grid);
        owner.AddChild(_viewport);

        LookAt(GVector3.Zero);
        ApplyCameraTransform();
    }

    protected override ImGuiWindowFlags Flags => ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    protected override void DrawContent()
    {
        NVector2 region = ImGui.GetContentRegionAvail();
        int width = Math.Max(1, (int)region.X);
        int height = Math.Max(1, (int)region.Y);
        if (_viewport.Size.X != width || _viewport.Size.Y != height)
        {
            _viewport.Size = new Vector2I(width, height);
        }

        IntPtr textureId = (IntPtr)_viewport.GetTexture().GetRid().Id;
        ImGui.Image(textureId, new NVector2(width, height));

        UpdateFlyCamera(ImGui.IsItemHovered());

        ApplyCameraTransform();

        // Keep the grid plane centred under the camera so the grid feels endless.
        _grid.GlobalPosition = new GVector3(_position.X, 0.0f, _position.Z);
    }

    private void UpdateFlyCamera(bool hovered)
    {
        // Right mouse button engages fly mode, exactly like the Unreal viewport.
        if (!_flying && hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            BeginFly();
        }
        else if (_flying && !Godot.Input.IsMouseButtonPressed(MouseButton.Right))
        {
            EndFly();
        }

        if (!_flying)
        {
            return;
        }

        // Mouselook: read how far the cursor moved from the anchor, then snap it back
        // so the OS cursor never actually travels and can't get stuck on anything.
        Vector2I mouse = GetMousePixels();
        Vector2I delta = mouse - _flyAnchor;
        _yaw -= delta.X * LookSensitivity;
        _pitch = Mathf.Clamp(_pitch - delta.Y * LookSensitivity, MinPitch, MaxPitch);
        Godot.Input.WarpMouse(_flyAnchor);

        ImGuiIOPtr io = ImGui.GetIO();
        if (io.MouseWheel != 0.0f)
        {
            _flySpeed = Mathf.Clamp(_flySpeed * Mathf.Pow(1.15f, io.MouseWheel), MinFlySpeed, MaxFlySpeed);
        }

        MoveFromKeyboard(io.DeltaTime);
    }

    private void MoveFromKeyboard(float delta)
    {
        float cosPitch = Mathf.Cos(_pitch);
        GVector3 forward = new(-Mathf.Sin(_yaw) * cosPitch, Mathf.Sin(_pitch), -Mathf.Cos(_yaw) * cosPitch);
        GVector3 right = new(Mathf.Cos(_yaw), 0.0f, -Mathf.Sin(_yaw));

        GVector3 direction = GVector3.Zero;
        if (Godot.Input.IsPhysicalKeyPressed(Key.W)) { direction += forward; }
        if (Godot.Input.IsPhysicalKeyPressed(Key.S)) { direction -= forward; }
        if (Godot.Input.IsPhysicalKeyPressed(Key.D)) { direction += right; }
        if (Godot.Input.IsPhysicalKeyPressed(Key.A)) { direction -= right; }
        if (Godot.Input.IsPhysicalKeyPressed(Key.E)) { direction += GVector3.Up; }
        if (Godot.Input.IsPhysicalKeyPressed(Key.Q)) { direction -= GVector3.Up; }

        if (direction == GVector3.Zero)
        {
            return;
        }

        float speed = _flySpeed;
        if (Godot.Input.IsPhysicalKeyPressed(Key.Shift))
        {
            speed *= BoostMultiplier;
        }

        _position += direction.Normalized() * speed * delta;
    }

    private void BeginFly()
    {
        _flying = true;
        _flyAnchor = GetMousePixels();
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.ConfinedHidden;
    }

    private void EndFly()
    {
        _flying = false;
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
        Godot.Input.WarpMouse(_flyAnchor);
    }

    private Vector2I GetMousePixels()
    {
        return DisplayServer.MouseGetPosition() - _owner.GetWindow().Position;
    }

    private void LookAt(GVector3 target)
    {
        GVector3 direction = (target - _position).Normalized();
        _pitch = Mathf.Clamp(Mathf.Asin(direction.Y), MinPitch, MaxPitch);
        _yaw = Mathf.Atan2(-direction.X, -direction.Z);
    }

    private void ApplyCameraTransform()
    {
        _camera.Position = _position;
        _camera.Rotation = new GVector3(_pitch, _yaw, 0.0f);
    }
}
