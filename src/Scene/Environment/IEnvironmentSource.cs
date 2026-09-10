using Godot;

namespace WorldMapStudio;

/// <summary>The clock state a source evaluates its curves against, captured once per blend so every
/// source in the same pass sees the same instant.</summary>
public readonly record struct EnvironmentTime(float DayFraction, double Elapsed);

/// <summary>
/// Implemented by a <see cref="SceneComponent"/> that contributes environment near itself.
/// <see cref="EnvironmentSystem"/> collects every loaded one from the scene, exactly like
/// <see cref="ILandscapeDeformer"/> is collected for terrain.
/// </summary>
public interface IEnvironmentSource
{
    /// <summary>
    /// The map-wide base a query starts from. Its <see cref="WeightAt"/> is never evaluated — a
    /// global source is always the first thing blended, at full weight, and only one is used: the
    /// one with the highest <see cref="GlobalPriority"/>, ties going to whichever
    /// <see cref="EnvironmentSystem"/> finds first.
    /// </summary>
    bool IsGlobal { get; }

    /// <summary>
    /// Which global source wins when more than one is loaded for a map. The blender keeps the
    /// highest. Only meaningful alongside <see cref="IsGlobal"/>; a lone global's value is moot.
    /// </summary>
    int GlobalPriority => 0;

    /// <summary>
    /// The tier a non-global source composes in. Sources fold in by ascending layer, and strongest
    /// first within a layer, so a lower layer is fully in place before a higher one blends over it.
    /// Ignored for a global source, which is always the base.
    /// </summary>
    int BlendLayer => 0;

    /// <summary>0 = no influence at this position, 1 = full override.</summary>
    float WeightAt(Vector3 worldPosition);

    EnvironmentValues Evaluate(in EnvironmentTime time);
}

/// <summary>
/// Optional companion to <see cref="IEnvironmentSource"/> for a spherical source, so the viewport can
/// draw and edit its radii the way Noggit draws light spheres.
/// </summary>
public interface IEnvironmentVolume
{
    float InnerRadius { get; }

    float OuterRadius { get; }

    /// <summary>Gizmo colour for the inner sphere — conventionally the source's own ambient colour.</summary>
    Color InnerColor { get; }

    /// <summary>Gizmo colour for the outer sphere — conventionally the source's own sun/diffuse colour.</summary>
    Color OuterColor { get; }
}

/// <summary>Distance-based falloff shapes a positional source can use for <see cref="IEnvironmentSource.WeightAt"/>.</summary>
public static class EnvironmentFalloff
{
    /// <summary>
    /// 1 inside <paramref name="inner"/>, 0 beyond <paramref name="outer"/>, linear between. Matches
    /// how Noggit weights a light sphere. Degenerates to a hard step at <paramref name="outer"/> when
    /// <paramref name="outer"/> does not exceed <paramref name="inner"/>, rather than dividing by zero.
    /// </summary>
    public static float Sphere(float distance, float inner, float outer)
    {
        if (outer <= inner)
        {
            return distance <= outer ? 1.0f : 0.0f;
        }

        if (distance >= outer)
        {
            return 0.0f;
        }

        if (distance <= inner)
        {
            return 1.0f;
        }

        return (outer - distance) / (outer - inner);
    }
}
