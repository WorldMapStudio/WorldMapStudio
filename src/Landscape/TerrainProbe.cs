using System;
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

    public TerrainProbe(SceneEntityRegistry scene)
    {
        _scene = scene;
    }

    /// <summary>Casts a ray against every loaded chunk and returns the nearest hit, if any.</summary>
    public bool TryHit(Vector3 rayOrigin, Vector3 rayDir, out Vector3 world)
    {
        world = default;
        float bestT = float.PositiveInfinity;
        bool hit = false;

        foreach (LandscapeChunk chunk in _scene.Entities.OfType<LandscapeChunk>())
        {
            if (!TryHitChunk(chunk, rayOrigin, rayDir, out float t, out Vector3 chunkWorld) || t >= bestT)
            {
                continue;
            }

            bestT = t;
            world = chunkWorld;
            hit = true;
        }

        return hit;
    }

    /// <summary>Bilinearly samples the built height of whichever loaded chunk covers this world point.</summary>
    public bool TryHeight(float worldX, float worldZ, out float height)
    {
        foreach (LandscapeChunk chunk in _scene.Entities.OfType<LandscapeChunk>())
        {
            Vector3 local = chunk.Transform.AffineInverse() * new Vector3(worldX, 0.0f, worldZ);
            Aabb bounds = chunk.LocalBounds;
            if (local.X < bounds.Position.X || local.Z < bounds.Position.Z ||
                local.X > bounds.End.X || local.Z > bounds.End.Z)
            {
                continue;
            }

            height = SampleChunkHeight(chunk.Output, bounds.Size.X, local.X, local.Z);
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

    private static bool TryHitChunk(LandscapeChunk chunk, Vector3 rayOrigin, Vector3 rayDir, out float bestT, out Vector3 bestWorld)
    {
        bestT = float.PositiveInfinity;
        bestWorld = default;

        Transform3D inverse = chunk.Transform.AffineInverse();
        Vector3 origin = inverse * rayOrigin;
        Vector3 dir = inverse.Basis * rayDir;
        if (!TryRayBox(origin, dir, chunk.LocalBounds, out _))
        {
            return false;
        }

        LandscapeChunkOutput output = chunk.Output;
        int resolution = output.HeightResolution;
        int quads = resolution - 1;
        float size = chunk.LocalBounds.Size.X;
        float step = size / quads;

        for (int z = 0; z < quads; z++)
        {
            for (int x = 0; x < quads; x++)
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

    private static bool TryRayBox(Vector3 origin, Vector3 dir, Aabb bounds, out float tHit)
    {
        tHit = 0.0f;
        Vector3 lo = bounds.Position;
        Vector3 hi = bounds.End;

        float[] oc = [origin.X, origin.Y, origin.Z];
        float[] dc = [dir.X, dir.Y, dir.Z];
        float[] loc = [lo.X, lo.Y, lo.Z];
        float[] hic = [hi.X, hi.Y, hi.Z];

        float tMin = float.NegativeInfinity;
        float tMax = float.PositiveInfinity;
        for (int a = 0; a < 3; a++)
        {
            if (Mathf.Abs(dc[a]) < 1e-8f)
            {
                if (oc[a] < loc[a] || oc[a] > hic[a])
                {
                    return false;
                }

                continue;
            }

            float t1 = (loc[a] - oc[a]) / dc[a];
            float t2 = (hic[a] - oc[a]) / dc[a];
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

        if (tMax < 0.0f)
        {
            return false;
        }

        tHit = tMin >= 0.0f ? tMin : tMax;
        return true;
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
