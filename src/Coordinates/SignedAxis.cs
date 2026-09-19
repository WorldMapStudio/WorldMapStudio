using Godot;

namespace WorldMapStudio;

/// <summary>
/// One of the six signed Godot axis directions. Used to describe, for each of the user's
/// three axes, which Godot direction it points along in <see cref="AxisConvention"/>.
/// </summary>
public enum SignedAxis
{
    PosX,
    NegX,
    PosY,
    NegY,
    PosZ,
    NegZ,
}

public static class SignedAxisExtensions
{
    /// <summary>The unit direction of this signed axis in Godot space.</summary>
    public static Vector3 Direction(this SignedAxis axis) => axis switch
    {
        SignedAxis.PosX => Vector3.Right,   // (+1, 0, 0)
        SignedAxis.NegX => Vector3.Left,    // (-1, 0, 0)
        SignedAxis.PosY => Vector3.Up,      // ( 0,+1, 0)
        SignedAxis.NegY => Vector3.Down,    // ( 0,-1, 0)
        SignedAxis.PosZ => Vector3.Back,    // ( 0, 0,+1)
        _ => Vector3.Forward,               // ( 0, 0,-1)
    };

    /// <summary>Which spatial Godot axis this points along: 0 = X, 1 = Y, 2 = Z.</summary>
    public static int Spatial(this SignedAxis axis) => (int)axis / 2;

    /// <summary>The sign of this axis: +1 for the positive variants, -1 for the negative ones.</summary>
    public static int Sign(this SignedAxis axis) => (int)axis % 2 == 0 ? 1 : -1;

    /// <summary>Rebuilds a signed axis from a spatial index (0/1/2) and a sign (+1/-1).</summary>
    public static SignedAxis FromSpatial(int spatial, int sign) =>
        (SignedAxis)(spatial * 2 + (sign < 0 ? 1 : 0));

    /// <summary>Flips the sign, keeping the same spatial axis.</summary>
    public static SignedAxis Flipped(this SignedAxis axis) => FromSpatial(axis.Spatial(), -axis.Sign());

    /// <summary>Short label such as "+Y", including the Godot role it plays (Up/Right/Forward/...).</summary>
    public static string Label(this SignedAxis axis) => axis switch
    {
        SignedAxis.PosX => "+X (Right)",
        SignedAxis.NegX => "-X (Left)",
        SignedAxis.PosY => "+Y (Up)",
        SignedAxis.NegY => "-Y (Down)",
        SignedAxis.PosZ => "+Z (Back)",
        _ => "-Z (Forward)",
    };
}
