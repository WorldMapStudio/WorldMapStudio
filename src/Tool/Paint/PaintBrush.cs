using Godot;

namespace WorldMapStudio;

/// <summary>The paint brush's settings. Owned by <see cref="PaintToolFactory"/>, so they outlive the tool
/// instance and are shared by the toolbar and <c>wms.paint</c>.</summary>
public sealed class PaintBrush
{
    public const float MinRadius = 0.1f;
    public const float MaxRadius = 512.0f;
    public const float MinOpacity = 0.01f;
    public const float MaxOpacity = 1.0f;

    private float _radius = 4.0f;
    private float _opacity = 0.35f;

    /// <summary>World-space radius.</summary>
    public float Radius
    {
        get => _radius;
        set => _radius = Mathf.Clamp(value, MinRadius, MaxRadius);
    }

    public float Opacity
    {
        get => _opacity;
        set => _opacity = Mathf.Clamp(value, MinOpacity, MaxOpacity);
    }

    public Color Color { get; set; } = Colors.White;

    public bool Erase { get; set; }

    /// <summary>Paint straight onto an object-mode image instead of projecting through the terrain.</summary>
    public bool PaintOnObject { get; set; } = true;

    public PaintBrush Clone() => new()
    {
        Radius = Radius,
        Opacity = Opacity,
        Color = Color,
        Erase = Erase,
        PaintOnObject = PaintOnObject,
    };
}
