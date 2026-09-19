using Godot;

namespace WorldMapStudio;

/// <summary>One dab handed to an <see cref="IStrokeTarget"/>. <see cref="Pressure"/> is 1 for a mouse.</summary>
public readonly record struct BrushDab(
    Vector3 Center,
    float Radius,
    float Strength,
    bool Invert,
    float Hardness,
    float Pressure = 1.0f);
