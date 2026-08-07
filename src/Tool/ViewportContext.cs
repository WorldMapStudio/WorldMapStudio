using Godot;
using NVector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>Per-frame viewport state handed to the active <see cref="ITool"/>.</summary>
public readonly struct ViewportContext(Camera3D camera, NVector2 imageMin, NVector2 imageSize, bool hovered, bool cameraFlying)
{
    public Camera3D Camera { get; } = camera;
    public NVector2 ImageMin { get; } = imageMin;
    public NVector2 ImageSize { get; } = imageSize;
    public bool Hovered { get; } = hovered;
    public bool CameraFlying { get; } = cameraFlying;
}
