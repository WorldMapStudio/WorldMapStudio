using Godot;
using NVector2 = System.Numerics.Vector2;
using GVector3 = Godot.Vector3;

namespace WorldMapStudio;

/// <summary>Per-frame viewport state handed to the active <see cref="ITool"/>.</summary>
public readonly struct ViewportContext(
    Camera3D camera,
    NVector2 imageMin,
    NVector2 imageSize,
    bool hovered,
    bool cameraFlying,
    GVector3 pointerRayOrigin,
    GVector3 pointerRayDir,
    bool terrainHit,
    GVector3 terrainPoint)
{
    public Camera3D Camera { get; } = camera;
    public NVector2 ImageMin { get; } = imageMin;
    public NVector2 ImageSize { get; } = imageSize;
    public bool Hovered { get; } = hovered;
    public bool CameraFlying { get; } = cameraFlying;

    /// <summary>The mouse pick ray for this frame, cast once by the viewport. Meaningless when
    /// <see cref="Hovered"/> is false.</summary>
    public GVector3 PointerRayOrigin { get; } = pointerRayOrigin;
    public GVector3 PointerRayDir { get; } = pointerRayDir;

    /// <summary>Whether <see cref="PointerRayOrigin"/>/<see cref="PointerRayDir"/> hit loaded terrain,
    /// and where — shared so a tool need not cast the same ray against the same terrain again.</summary>
    public bool TerrainHit { get; } = terrainHit;
    public GVector3 TerrainPoint { get; } = terrainPoint;
}
