using Godot;
using ImGuiNET;
using GVector3 = Godot.Vector3;

namespace WorldMapStudio;

/// <summary>
/// Unreal-style fly camera: hold the right mouse button to fly. While held, moving the
/// mouse looks around, W/A/S/D moves, E/Q rises and falls, Shift boosts, and the wheel
/// changes fly speed. The cursor is hidden and pinned in place for the duration, so it
/// never physically moves or snags on other UI.
/// </summary>
public sealed class FlyCamera
{
    private const float LookSensitivity = 0.0025f;
    private const float MinPitch = -1.55f;
    private const float MaxPitch = 1.55f;
    private const float MinFlySpeed = 0.5f;
    private const float MaxFlySpeed = 200.0f;
    private const float BoostMultiplier = 4.0f;

    private readonly Node _owner;

    public GVector3 Position { get; private set; }
    public bool IsFlying { get; private set; }

    private float _yaw;
    private float _pitch;
    private float _flySpeed = 8.0f;
    private Vector2I _flyAnchor;

    public FlyCamera(Node owner, GVector3 initialPosition)
    {
        _owner = owner;
        Position = initialPosition;
    }

    /// <summary>Teleports the camera, keeping its current orientation.</summary>
    public void MoveTo(GVector3 position)
    {
        Position = position;
    }

    /// <summary>The full camera state, for persisting across sessions.</summary>
    public CameraPose Pose => new(Position, _yaw, _pitch, _flySpeed);

    /// <summary>Restores a snapshot from <see cref="Pose"/>, re-clamping to the current limits.</summary>
    public void SetPose(CameraPose pose)
    {
        Position = pose.Position;
        _yaw = pose.Yaw;
        _pitch = Mathf.Clamp(pose.Pitch, MinPitch, MaxPitch);
        if (pose.FlySpeed > 0.0f)
        {
            _flySpeed = Mathf.Clamp(pose.FlySpeed, MinFlySpeed, MaxFlySpeed);
        }
    }

    public void LookAt(GVector3 target)
    {
        GVector3 direction = (target - Position).Normalized();
        _pitch = Mathf.Clamp(Mathf.Asin(direction.Y), MinPitch, MaxPitch);
        _yaw = Mathf.Atan2(-direction.X, -direction.Z);
    }

    /// <summary>Advance the camera by one frame. <paramref name="hovered"/> gates whether a
    /// right-click may begin flying; once flying, input is captured regardless.</summary>
    public void Update(bool hovered)
    {
        if (!IsFlying && hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            BeginFly();
        }
        else if (IsFlying && !Godot.Input.IsMouseButtonPressed(MouseButton.Right))
        {
            EndFly();
        }

        if (!IsFlying)
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

    public void ApplyTo(Camera3D camera)
    {
        camera.Position = Position;
        camera.Rotation = new GVector3(_pitch, _yaw, 0.0f);
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

        Position += direction.Normalized() * speed * delta;
    }

    private void BeginFly()
    {
        IsFlying = true;
        _flyAnchor = GetMousePixels();
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.ConfinedHidden;
    }

    private void EndFly()
    {
        IsFlying = false;
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
        Godot.Input.WarpMouse(_flyAnchor);
    }

    private Vector2I GetMousePixels()
    {
        return DisplayServer.MouseGetPosition() - _owner.GetWindow().Position;
    }
}
