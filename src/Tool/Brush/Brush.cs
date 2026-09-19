using Godot;

namespace WorldMapStudio;

/// <summary>General stroke settings shared by every brush tool. Owned by each tool's factory, so they
/// outlive the tool instance and are shared by its toolbar and script API.</summary>
public sealed class Brush
{
    public const float MinRadius = 0.1f;
    public const float MaxRadius = 512.0f;
    public const float MinStrength = 0.01f;
    public const float MaxStrength = 1.0f;
    public const float MinSpacing = 0.02f;
    public const float MaxSpacing = 2.0f;
    public const float MinAirbrushRate = 1.0f;
    public const float MaxAirbrushRate = 240.0f;
    public const int MaxSprayCount = 64;

    private float _radius = 4.0f;
    private float _strength = 0.35f;
    private float _hardness;
    private float _spacing = 0.25f;
    private float _airbrushRate = 62.5f;
    private int _sprayCount = 8;
    private float _sprayScatter = 1.0f;

    /// <summary>World-space radius.</summary>
    public float Radius
    {
        get => _radius;
        set => _radius = Mathf.Clamp(value, MinRadius, MaxRadius);
    }

    /// <summary>Per-dab amount.</summary>
    public float Strength
    {
        get => _strength;
        set => _strength = Mathf.Clamp(value, MinStrength, MaxStrength);
    }

    /// <summary>0 is a full smooth falloff from the centre, 1 a hard-edged disc.</summary>
    public float Hardness
    {
        get => _hardness;
        set => _hardness = Mathf.Clamp(value, 0.0f, 1.0f);
    }

    /// <summary>Distance between dabs along the path, as a fraction of the radius.</summary>
    public float Spacing
    {
        get => _spacing;
        set => _spacing = Mathf.Clamp(value, MinSpacing, MaxSpacing);
    }

    /// <summary>Dabs per second while the pointer is held still.</summary>
    public float AirbrushRate
    {
        get => _airbrushRate;
        set => _airbrushRate = Mathf.Clamp(value, MinAirbrushRate, MaxAirbrushRate);
    }

    /// <summary>Scatters each dab into <see cref="SprayCount"/> small dabs.</summary>
    public bool Spray { get; set; }

    public int SprayCount
    {
        get => _sprayCount;
        set => _sprayCount = Mathf.Clamp(value, 1, MaxSprayCount);
    }

    /// <summary>How far spray dabs may land from the centre, as a fraction of the radius.</summary>
    public float SprayScatter
    {
        get => _sprayScatter;
        set => _sprayScatter = Mathf.Clamp(value, 0.0f, 1.0f);
    }

    /// <summary>Reverses what a dab does; each target decides what that means (erase, remove).</summary>
    public bool Invert { get; set; }

    public Brush Clone() => new()
    {
        Radius = Radius,
        Strength = Strength,
        Hardness = Hardness,
        Spacing = Spacing,
        AirbrushRate = AirbrushRate,
        Spray = Spray,
        SprayCount = SprayCount,
        SprayScatter = SprayScatter,
        Invert = Invert,
    };
}
