using System;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Where a brush lands under the pointer: the terrain the viewport already hit, then optionally the world
/// Y=0 plane, or one custom plane instead of both. A hit is only accepted where <see cref="Accepts"/> says
/// the tool can paint.
/// </summary>
public sealed class BrushSurface
{
    private readonly TerrainProbe _terrain;

    public BrushSurface(TerrainProbe terrain)
    {
        _terrain = terrain;
    }

    /// <summary>Whether a world point is somewhere the tool can paint. Null accepts everything.</summary>
    public Func<Vector3, bool>? Accepts { get; set; }

    /// <summary>Whether a ray that misses the terrain falls back to the world Y=0 plane.</summary>
    public bool FallbackPlane { get; set; } = true;

    /// <summary>A plane (the transform's local Y=0) that replaces the terrain and fallback plane.</summary>
    public Transform3D? Plane { get; set; }

    /// <summary>Where a world point is drawn: on the custom plane, or dropped onto the terrain.</summary>
    public Vector3 Lift(Vector3 world)
    {
        if (Plane is { } plane)
        {
            Vector3 local = plane.AffineInverse() * world;
            return plane * new Vector3(local.X, 0.0f, local.Z);
        }

        return _terrain.DropToHeight(world);
    }

    public bool TryHit(in ViewportContext context, out Vector3 world)
    {
        world = default;
        if (!context.Hovered)
        {
            return false;
        }

        if (Plane is { } plane)
        {
            return TryHitPlane(context, plane, out world) && Accept(world);
        }

        // The viewport already cast this ray against the terrain for its pointer; reuse the hit
        // rather than casting it a second time in the same frame.
        if (context.TerrainHit && Accept(context.TerrainPoint))
        {
            world = context.TerrainPoint;
            return true;
        }

        return FallbackPlane && TryHitPlane(context, Transform3D.Identity, out world) && Accept(world);
    }

    private bool Accept(Vector3 world) => Accepts?.Invoke(world) ?? true;

    private static bool TryHitPlane(in ViewportContext context, Transform3D plane, out Vector3 world)
    {
        world = default;
        Transform3D inverse = plane.AffineInverse();
        Vector3 origin = inverse * context.PointerRayOrigin;
        Vector3 direction = inverse.Basis * context.PointerRayDir;
        if (Mathf.Abs(direction.Y) < 1e-6f)
        {
            return false;
        }

        float t = -origin.Y / direction.Y;
        if (t < 0.0f)
        {
            return false;
        }

        world = plane * (origin + (direction * t));
        return true;
    }
}
