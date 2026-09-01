using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Ray-casts and height-samples against the loaded <see cref="LandscapeChunk"/> meshes. Shared by
/// anything that needs to sit on the terrain surface — the paint brush, and network-editing tools
/// placing or drawing vertices that follow the ground.
/// </summary>
public sealed class TerrainProbe
{
    private readonly SceneEntityRegistry _scene;

    // Reused across calls (TryHit runs every frame the viewport is hovered) so ordering candidates
    // by box distance costs one List.Clear() rather than an allocation per pointer move.
    private readonly List<(LandscapeChunk Chunk, float TMin, float TMax, float BoxT)> _candidates = [];

    public TerrainProbe(SceneEntityRegistry scene)
    {
        _scene = scene;
    }

    /// <summary>
    /// Nearest ray-vs-terrain hit across every loaded chunk, if any.
    ///
    /// Broad-phase first, over every chunk, then only as many narrow-phase (triangle) tests as
    /// necessary: chunks are ordered nearest-box-first, and once a real hit is found, any chunk whose
    /// box cannot possibly be closer is skipped outright. A chunk's box is a nominal
    /// ±<see cref="LandscapeGrid.NominalHeightExtent"/> slab far taller than its actual terrain, so
    /// without this a ray from a camera sitting inside that slab would otherwise pay for a full
    /// triangle sweep of every chunk it merely passes over, not just the one it actually lands on.
    /// </summary>
    public bool TryHit(Vector3 rayOrigin, Vector3 rayDir, out Vector3 world)
    {
        world = default;
        _candidates.Clear();

        foreach (LandscapeChunk chunk in _scene.Entities.OfType<LandscapeChunk>())
        {
            if (TryRayBox(rayOrigin, rayDir, chunk.WorldBounds, out float tMin, out float tMax))
            {
                float boxT = tMin >= 0.0f ? tMin : tMax;
                _candidates.Add((chunk, tMin, tMax, boxT));
            }
        }

        _candidates.Sort(static (a, b) => a.BoxT.CompareTo(b.BoxT));

        float bestT = float.PositiveInfinity;
        bool hit = false;
        foreach ((LandscapeChunk chunk, float tMin, float tMax, float boxT) in _candidates)
        {
            if (boxT >= bestT)
            {
                break;
            }

            if (TryHitChunk(chunk, rayOrigin, rayDir, tMin, tMax, out float t, out Vector3 chunkWorld) && t < bestT)
            {
                bestT = t;
                world = chunkWorld;
                hit = true;
            }
        }

        return hit;
    }

    /// <summary>Bilinearly samples the built height of whichever loaded chunk covers this world point.</summary>
    public bool TryHeight(float worldX, float worldZ, out float height)
    {
        foreach (LandscapeChunk chunk in _scene.Entities.OfType<LandscapeChunk>())
        {
            // Chunks are placed with an identity basis (see the LandscapeChunk constructor), so
            // world->local is a plain subtraction of the origin. AffineInverse() here was a full
            // matrix inverse per chunk per call, and this runs once per brush-outline point — the
            // Paint tool alone probes it ~50 times a frame while the pointer is over the viewport.
            Vector3 origin = chunk.Transform.Origin;
            float localX = worldX - origin.X;
            float localZ = worldZ - origin.Z;
            Aabb bounds = chunk.LocalBounds;
            if (localX < bounds.Position.X || localZ < bounds.Position.Z ||
                localX > bounds.End.X || localZ > bounds.End.Z)
            {
                continue;
            }

            height = SampleChunkHeight(chunk.Output, bounds.Size.X, localX, localZ);
            return true;
        }

        height = 0.0f;
        return false;
    }

    /// <summary>Convenience for callers that just want to drop a point onto the ground, or leave it
    /// where it is when no terrain is loaded under it.</summary>
    public Vector3 DropToHeight(Vector3 world)
    {
        if (TryHeight(world.X, world.Z, out float height))
        {
            world.Y = height;
        }

        return world;
    }

    /// <summary>Falls back to the world Y=0 plane for a ray that missed every loaded chunk (e.g. no
    /// terrain loaded there yet), so placement still works over bare ground.</summary>
    public static bool TryGroundPlane(Vector3 origin, Vector3 dir, out Vector3 world)
    {
        if (Mathf.Abs(dir.Y) < 1e-6f)
        {
            world = default;
            return false;
        }

        float t = -origin.Y / dir.Y;
        if (t < 0.0f)
        {
            world = default;
            return false;
        }

        world = origin + dir * t;
        return true;
    }

    // tMin/tMax are the caller's already-computed world-space box entry/exit — passed in rather than
    // recomputed so the (cheap but non-free) box test at the bottom of every WorldBounds access
    // happens once per candidate, not once per candidate per call site.
    private static bool TryHitChunk(LandscapeChunk chunk, Vector3 rayOrigin, Vector3 rayDir, float tMin, float tMax, out float bestT, out Vector3 bestWorld)
    {
        bestT = float.PositiveInfinity;
        bestWorld = default;

        // Chunks are placed with an identity basis (see the constructor), so the world-space box
        // entry/exit computed by the caller carries over unchanged into this local space: the ray
        // parametrization is identical, just offset by the chunk's origin.
        Transform3D inverse = chunk.Transform.AffineInverse();
        Vector3 origin = inverse * rayOrigin;
        Vector3 dir = inverse.Basis * rayDir;

        LandscapeChunkOutput output = chunk.Output;
        int resolution = output.HeightResolution;
        int quads = resolution - 1;
        float size = chunk.LocalBounds.Size.X;
        float step = size / quads;

        // The box is a nominal ±NominalHeightExtent slab, far taller than the real terrain it wraps,
        // so its entry/exit are usually where the ray crosses the *sides* of that slab rather than
        // the actual surface. What matters here is only the horizontal (X/Z) span the ray sweeps
        // through the chunk between those two points — every quad the ray could possibly cross lies
        // within the axis-aligned bounding box of that span, so quads outside it can never be hit and
        // are skipped rather than tested. A ray starting inside the slab (the common case: the camera
        // usually sits within a chunk's nominal height range) enters at its current position (t=0).
        float enterT = Mathf.Max(tMin, 0.0f);
        Vector3 enter = origin + (dir * enterT);
        Vector3 exit = origin + (dir * tMax);

        int xStart = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(enter.X, exit.X) / step), 0, quads - 1);
        int xEnd = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(enter.X, exit.X) / step), 0, quads - 1);
        int zStart = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(enter.Z, exit.Z) / step), 0, quads - 1);
        int zEnd = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(enter.Z, exit.Z) / step), 0, quads - 1);

        for (int z = zStart; z <= zEnd; z++)
        {
            for (int x = xStart; x <= xEnd; x++)
            {
                Vector3 topLeft = Vertex(output, x, z, step);
                Vector3 topRight = Vertex(output, x + 1, z, step);
                Vector3 bottomLeft = Vertex(output, x, z + 1, step);
                Vector3 bottomRight = Vertex(output, x + 1, z + 1, step);

                if (TryRayTriangle(origin, dir, topLeft, topRight, bottomLeft, out float t) && t < bestT)
                {
                    bestT = t;
                    bestWorld = chunk.Transform * (origin + (dir * t));
                }

                if (TryRayTriangle(origin, dir, topRight, bottomRight, bottomLeft, out t) && t < bestT)
                {
                    bestT = t;
                    bestWorld = chunk.Transform * (origin + (dir * t));
                }
            }
        }

        return bestT < float.PositiveInfinity;
    }

    private static Vector3 Vertex(LandscapeChunkOutput output, int x, int z, float step) =>
        new(x * step, output.HeightAt(x, z), z * step);

    // Indexes Vector3 components directly rather than copying them into temporary float[]s (as
    // MeshPicking.TryRayBox already does): this runs once per loaded chunk every time the pointer
    // moves, and four array allocations per chunk per call was pure GC pressure bought for nothing.
    private static bool TryRayBox(Vector3 origin, Vector3 dir, Aabb bounds, out float tMin, out float tMax)
    {
        tMin = float.NegativeInfinity;
        tMax = float.PositiveInfinity;
        Vector3 lo = bounds.Position;
        Vector3 hi = bounds.End;

        for (int a = 0; a < 3; a++)
        {
            float o = origin[a];
            float d = dir[a];
            if (Mathf.Abs(d) < 1e-8f)
            {
                if (o < lo[a] || o > hi[a])
                {
                    return false;
                }

                continue;
            }

            float t1 = (lo[a] - o) / d;
            float t2 = (hi[a] - o) / d;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            if (tMin > tMax)
            {
                return false;
            }
        }

        return tMax >= 0.0f;
    }

    private static bool TryRayTriangle(Vector3 origin, Vector3 dir, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        const float Epsilon = 1e-6f;
        t = 0.0f;

        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        Vector3 h = dir.Cross(edge2);
        float det = edge1.Dot(h);
        if (Mathf.Abs(det) < Epsilon)
        {
            return false;
        }

        float invDet = 1.0f / det;
        Vector3 s = origin - a;
        float u = invDet * s.Dot(h);
        if (u < 0.0f || u > 1.0f)
        {
            return false;
        }

        Vector3 q = s.Cross(edge1);
        float v = invDet * dir.Dot(q);
        if (v < 0.0f || u + v > 1.0f)
        {
            return false;
        }

        t = invDet * edge2.Dot(q);
        return t >= 0.0f;
    }

    private static float SampleChunkHeight(LandscapeChunkOutput output, float chunkSize, float x, float z)
    {
        int resolution = output.HeightResolution;
        int quads = resolution - 1;
        float gx = Mathf.Clamp(x / chunkSize * quads, 0.0f, quads);
        float gz = Mathf.Clamp(z / chunkSize * quads, 0.0f, quads);
        int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, resolution - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(gz), 0, resolution - 1);
        int x1 = Mathf.Min(x0 + 1, resolution - 1);
        int z1 = Mathf.Min(z0 + 1, resolution - 1);
        float tx = gx - x0;
        float tz = gz - z0;

        float a = Mathf.Lerp(output.HeightAt(x0, z0), output.HeightAt(x1, z0), tx);
        float b = Mathf.Lerp(output.HeightAt(x0, z1), output.HeightAt(x1, z1), tx);
        return Mathf.Lerp(a, b, tz);
    }
}
