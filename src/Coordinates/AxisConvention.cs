using Godot;

namespace WorldMapStudio;

/// <summary>
/// The bridge between the coordinate system the <em>user</em> works in and Godot's internal
/// coordinate system. The user thinks purely in their own axes (for example "X is up, Y is
/// right, Z is forward"); every value that crosses into or out of Godot is routed through this
/// mapping so nothing outside the user-facing layer has to know the difference.
///
/// The mapping is a change of basis: <see cref="ToGodotBasis"/> is the matrix <c>M</c> whose
/// columns are the Godot directions of the user's X, Y and Z axes. A user vector <c>v</c> is
/// therefore <c>M · v</c> in Godot space, and a Godot vector is <c>Mᵀ · v</c> back in user space
/// (the mapping is always orthonormal, so the inverse is just the transpose).
///
/// It is always a signed permutation of the three axes, so it is guaranteed invertible:
/// assigning a user axis to a Godot axis that is already taken swaps the two, keeping it a
/// bijection. New Godot-facing systems should convert through the <c>ToGodot</c>/<c>ToUser</c>
/// helpers rather than touching Godot values directly.
/// </summary>
public sealed class AxisConvention
{
    private SignedAxis _x = SignedAxis.PosX;
    private SignedAxis _y = SignedAxis.PosY;
    private SignedAxis _z = SignedAxis.PosZ;

    private Basis _toGodot = Basis.Identity;
    private Basis _toUser = Basis.Identity;

    /// <summary>A convention identical to Godot's own axes (the default: no remapping).</summary>
    public static AxisConvention GodotDefault => new();

    /// <summary>The Godot direction the user's X axis points along.</summary>
    public SignedAxis X => _x;

    /// <summary>The Godot direction the user's Y axis points along.</summary>
    public SignedAxis Y => _y;

    /// <summary>The Godot direction the user's Z axis points along.</summary>
    public SignedAxis Z => _z;

    /// <summary>True when this is the identity mapping (user axes equal Godot axes).</summary>
    public bool IsGodotDefault => _x == SignedAxis.PosX && _y == SignedAxis.PosY && _z == SignedAxis.PosZ;

    /// <summary>The change-of-basis matrix from user space to Godot space (columns = user axes).</summary>
    public Basis ToGodotBasis => _toGodot;

    /// <summary>The Godot direction of the user's axis <paramref name="userIndex"/> (0 = X, 1 = Y, 2 = Z).</summary>
    public Vector3 UserAxis(int userIndex) => userIndex switch
    {
        0 => _toGodot.X,
        1 => _toGodot.Y,
        _ => _toGodot.Z,
    };

    /// <summary>Reads the signed axis assigned to a user axis (0 = X, 1 = Y, 2 = Z).</summary>
    public SignedAxis Get(int userIndex) => userIndex switch
    {
        0 => _x,
        1 => _y,
        _ => _z,
    };

    /// <summary>
    /// Points a user axis at <paramref name="target"/>. If another user axis already uses that
    /// spatial axis, the two swap (the displaced axis inherits the freed spatial axis, keeping
    /// its own sign), so the convention always stays a valid, invertible permutation.
    /// </summary>
    public void AssignUserAxis(int userIndex, SignedAxis target)
    {
        SignedAxis current = Get(userIndex);
        if (current == target)
        {
            return;
        }

        for (int other = 0; other < 3; other++)
        {
            if (other != userIndex && Get(other).Spatial() == target.Spatial())
            {
                Set(other, SignedAxisExtensions.FromSpatial(current.Spatial(), Get(other).Sign()));
            }
        }

        Set(userIndex, target);
        Rebuild();
    }

    // ---- Conversions -------------------------------------------------------

    /// <summary>Converts a position or vector from user space to Godot space.</summary>
    public Vector3 ToGodot(Vector3 userVec) => _toGodot * userVec;

    /// <summary>Converts a position or vector from Godot space to user space.</summary>
    public Vector3 ToUser(Vector3 godotVec) => _toUser * godotVec;

    /// <summary>Converts an orientation from user space to Godot space.</summary>
    public Basis ToGodot(Basis userBasis) => _toGodot * userBasis * _toUser;

    /// <summary>Converts an orientation from Godot space to user space.</summary>
    public Basis ToUser(Basis godotBasis) => _toUser * godotBasis * _toGodot;

    /// <summary>Converts a full transform from user space to Godot space.</summary>
    public Transform3D ToGodot(Transform3D userXform) => new(ToGodot(userXform.Basis), _toGodot * userXform.Origin);

    /// <summary>Converts a full transform from Godot space to user space.</summary>
    public Transform3D ToUser(Transform3D godotXform) => new(ToUser(godotXform.Basis), _toUser * godotXform.Origin);

    /// <summary>Converts a rotation from user space to Godot space.</summary>
    public Quaternion ToGodot(Quaternion userRotation) => ToGodot(new Basis(userRotation)).GetRotationQuaternion();

    /// <summary>Converts a rotation from Godot space to user space.</summary>
    public Quaternion ToUser(Quaternion godotRotation) => ToUser(new Basis(godotRotation)).GetRotationQuaternion();

    private void Set(int userIndex, SignedAxis value)
    {
        switch (userIndex)
        {
            case 0: _x = value; break;
            case 1: _y = value; break;
            default: _z = value; break;
        }
    }

    // Columns of M are the Godot directions of the user's X/Y/Z axes; because M is an
    // orthonormal signed permutation, its inverse is simply its transpose.
    private void Rebuild()
    {
        Basis m = Basis.Identity;
        m.X = _x.Direction();
        m.Y = _y.Direction();
        m.Z = _z.Direction();
        _toGodot = m;
        _toUser = m.Transposed();
    }
}
