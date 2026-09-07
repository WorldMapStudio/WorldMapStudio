using Godot;

namespace WorldMapStudio;

/// <summary>Per-frame viewport state handed to each <see cref="IViewportHeaderItem"/> as it draws.
/// One frame behind the current input, like <see cref="ViewportPointer"/>: the header draws before
/// the fly camera has consumed this frame's movement.</summary>
public readonly struct ViewportHeaderContext(Camera3D camera, bool cameraFlying)
{
    /// <summary>The live viewport camera. Its <c>GlobalTransform</c> is the camera's world pose.</summary>
    public Camera3D Camera { get; } = camera;

    public bool CameraFlying { get; } = cameraFlying;
}
