using Godot;
using NVector2 = System.Numerics.Vector2;
using GVector3 = Godot.Vector3;
using GVector4 = Godot.Vector4;

namespace WorldMapStudio;

/// <summary>
/// Projects world points to viewport-image pixels with the same result as
/// <see cref="Camera3D.UnprojectPosition"/>, but built once per frame from the camera's view and
/// projection matrices so a caller plotting many points — a brush ring, a target outline — pays two
/// marshalled Godot calls for the whole frame rather than two per point.
/// </summary>
public readonly struct ViewportProjector
{
    private readonly Transform3D _view;
    private readonly Projection _projection;
    private readonly GVector3 _forward;
    private readonly GVector3 _eye;
    private readonly float _near;
    private readonly NVector2 _imageMin;
    private readonly float _width;
    private readonly float _height;

    public ViewportProjector(Camera3D camera, NVector2 imageMin, NVector2 imageSize)
    {
        Transform3D cameraTransform = camera.GetCameraTransform();
        _view = cameraTransform.AffineInverse();
        _projection = camera.GetCameraProjection();
        _forward = -cameraTransform.Basis.Column2.Normalized();
        _eye = cameraTransform.Origin;
        _near = camera.Near;
        _imageMin = imageMin;
        _width = imageSize.X;
        _height = imageSize.Y;
    }

    /// <summary>Pixel position of <paramref name="world"/> in the viewport image, or false when it
    /// sits at or behind the near plane, where a screen position is undefined.</summary>
    public bool TryProject(GVector3 world, out NVector2 screen)
    {
        // Matches Camera3D.IsPositionBehind: behind when it is nearer than the near plane along the
        // view axis.
        if (_forward.Dot(world - _eye) < _near)
        {
            screen = default;
            return false;
        }

        GVector3 viewPos = _view * world;
        GVector4 clip = _projection * new GVector4(viewPos.X, viewPos.Y, viewPos.Z, 1.0f);
        if (clip.W <= 0.0f)
        {
            screen = default;
            return false;
        }

        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;
        screen = new NVector2(
            _imageMin.X + (((ndcX * 0.5f) + 0.5f) * _width),
            _imageMin.Y + (((-ndcY * 0.5f) + 0.5f) * _height));
        return true;
    }
}
