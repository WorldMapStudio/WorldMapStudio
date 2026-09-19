using System;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Ray-casts and height-samples against the loaded terrain, working off the built
/// <see cref="LandscapeChunkOutput"/> the <see cref="LandscapeChunkIndex"/> holds per chunk rather
/// than the Godot meshes. Shared by anything that needs to sit on the terrain surface — the paint
/// brush, and network-editing tools placing or drawing vertices that follow the ground. Picking stays
/// per chunk: batching how chunks are drawn must not coarsen it.
/// </summary>
public sealed class TerrainProbe
{
    private readonly LandscapeSystem _landscape;

    // Reused across calls (TryHit runs every frame the viewport is hovered) so ordering candidates
    // by box distance costs one List.Clear() rather than an allocation per pointer move.
    private readonly System.Collections.Generic.List<Candidate> _candidates = [];

    public TerrainProbe(LandscapeSystem landscape)
    {
        _landscape = landscape;
    }

    internal readonly record struct Candidate(
        ChunkCoord Coord, LandscapeChunkOutput Output, Vector3 Origin, float ChunkSize, float TMin, float TMax, float BoxT);

    // A near-horizontal ray can cross a lot of chunks; this caps the grid walk well past any real
    // view distance (a chunk is tens of units, so this is millions of units of reach).
    private const int MaxRaySteps = 4096;

    /// <summary>
    /// Nearest ray-vs-terrain hit across every loaded chunk, if any.
    ///
    /// The candidate set is just the chunks the ray's ground track actually crosses, walked in
    /// near-to-far order along the grid; they are then ordered nearest-box-first, and once a real hit
    /// is found, any chunk whose box cannot possibly be closer is skipped outright.
    /// </summary>
    public bool TryHit(Vector3 rayOrigin, Vector3 rayDir, out Vector3 world)
    {
        world = default;
        _candidates.Clear();

        CollectCandidates(rayOrigin, rayDir);

        _candidates.Sort(static (a, b) => a.BoxT.CompareTo(b.BoxT));

        float bestT = float.PositiveInfinity;
        bool hit = false;
        foreach (Candidate candidate in _candidates)
        {
            if (candidate.BoxT >= bestT)
            {
                break;
            }

            if (TryHitChunk(candidate, rayOrigin, rayDir, out float t, out Vector3 chunkWorld) && t < bestT)
            {
                bestT = t;
                world = chunkWorld;
                hit = true;
            }
        }

        return hit;
    }

    // Fills _candidates with the loaded chunks the ray's X/Z track crosses, via an Amanatides-Woo
    // grid walk from the ray origin's cell. Nothing is loaded when there is no grid, so there are no
    // candidates either.
    private void CollectCandidates(Vector3 rayOrigin, Vector3 rayDir)
    {
        if (_landscape.Grid is not { } grid)
        {
            return;
        }

        LandscapeChunkIndex index = _landscape.ChunkIndex;
        float size = grid.ChunkSize;
        ChunkCoord start = grid.CoordAt(rayOrigin);
        Vector3 cellOrigin = grid.OriginOf(start);

        int cx = start.X;
        int cy = start.Y;
        int stepX = rayDir.X > 0.0f ? 1 : rayDir.X < 0.0f ? -1 : 0;
        int stepZ = rayDir.Z > 0.0f ? 1 : rayDir.Z < 0.0f ? -1 : 0;

        float fx = (rayOrigin.X - cellOrigin.X) / size;
        float fz = (rayOrigin.Z - cellOrigin.Z) / size;

        float tMaxX = stepX == 0 ? float.PositiveInfinity : ((stepX > 0 ? 1.0f - fx : fx) * size) / Mathf.Abs(rayDir.X);
        float tMaxZ = stepZ == 0 ? float.PositiveInfinity : ((stepZ > 0 ? 1.0f - fz : fz) * size) / Mathf.Abs(rayDir.Z);
        float tDeltaX = stepX == 0 ? float.PositiveInfinity : size / Mathf.Abs(rayDir.X);
        float tDeltaZ = stepZ == 0 ? float.PositiveInfinity : size / Mathf.Abs(rayDir.Z);

        for (int steps = 0; steps < MaxRaySteps; steps++)
        {
            var coord = new ChunkCoord(cx, cy);
            if (index.OutputAt(coord) is { } output)
            {
                AddCandidate(coord, output, grid.OriginOf(coord), size, grid.BoundsOf(coord), rayOrigin, rayDir);
            }

            if (stepX == 0 && stepZ == 0)
            {
                break;
            }

            if (tMaxX < tMaxZ)
            {
                cx += stepX;
                tMaxX += tDeltaX;
            }
            else
            {
                cy += stepZ;
                tMaxZ += tDeltaZ;
            }
        }
    }

    private void AddCandidate(ChunkCoord coord, LandscapeChunkOutput output, Vector3 origin, float chunkSize, Aabb bounds, Vector3 rayOrigin, Vector3 rayDir)
    {
        if (TryRayBox(rayOrigin, rayDir, bounds, out float tMin, out float tMax))
        {
            float boxT = tMin >= 0.0f ? tMin : tMax;
            _candidates.Add(new Candidate(coord, output, origin, chunkSize, tMin, tMax, boxT));
        }
    }

    /// <summary>Bilinearly samples the built height of whichever loaded chunk covers this world point.</summary>
    public bool TryHeight(float worldX, float worldZ, out float height)
    {
        height = 0.0f;
        if (_landscape.Grid is not { } grid)
        {
            return false;
        }

        ChunkCoord coord = grid.CoordAt(new Vector3(worldX, 0.0f, worldZ));
        if (_landscape.ChunkIndex.OutputAt(coord) is not { } output)
        {
            return false;
        }

        Vector3 origin = grid.OriginOf(coord);
        height = SampleChunkHeight(output, grid.ChunkSize, worldX - origin.X, worldZ - origin.Z);
        return true;
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

    /// <summary>Falls back to the world Y=0 plane for a ray that missed every loaded chunk, so
    /// placement still works over bare ground.</summary>
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

    // Chunks sit on the grid with an identity basis, so world->local is a plain subtraction of the
    // chunk's world origin and the caller's world-space box entry/exit carry over unchanged.
    internal static bool TryHitChunk(Candidate candidate, Vector3 rayOrigin, Vector3 rayDir, out float bestT, out Vector3 bestWorld)
    {
        bestT = float.PositiveInfinity;
        bestWorld = default;

        Vector3 origin = rayOrigin - candidate.Origin;
        Vector3 dir = rayDir;

        LandscapeChunkOutput output = candidate.Output;
        int resolution = output.HeightResolution;
        int quads = resolution - 1;
        float step = candidate.ChunkSize / quads;

        float enterT = Mathf.Max(candidate.TMin, 0.0f);
        Vector3 enter = origin + (dir * enterT);
        Vector3 exit = origin + (dir * candidate.TMax);

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

                float t;
                if (output.HasCellCentres)
                {
                    var centre = new Vector3((x + 0.5f) * step, output.CentreHeightAt(x, z), (z + 0.5f) * step);
                    Vector3 from = topLeft;
                    foreach (Vector3 to in (ReadOnlySpan<Vector3>)[topRight, bottomRight, bottomLeft, topLeft])
                    {
                        if (TryRayTriangle(origin, dir, centre, from, to, out t) && t < bestT)
                        {
                            bestT = t;
                            bestWorld = candidate.Origin + origin + (dir * t);
                        }

                        from = to;
                    }

                    continue;
                }

                if (TryRayTriangle(origin, dir, topLeft, topRight, bottomLeft, out t) && t < bestT)
                {
                    bestT = t;
                    bestWorld = candidate.Origin + origin + (dir * t);
                }

                if (TryRayTriangle(origin, dir, topRight, bottomRight, bottomLeft, out t) && t < bestT)
                {
                    bestT = t;
                    bestWorld = candidate.Origin + origin + (dir * t);
                }
            }
        }

        return bestT < float.PositiveInfinity;
    }

    private static Vector3 Vertex(LandscapeChunkOutput output, int x, int z, float step) =>
        new(x * step, output.HeightAt(x, z), z * step);

    internal static bool TryRayBox(Vector3 origin, Vector3 dir, Aabb bounds, out float tMin, out float tMax)
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
        int quads = output.HeightResolution - 1;
        float gx = Mathf.Clamp(x / chunkSize * quads, 0.0f, quads);
        float gz = Mathf.Clamp(z / chunkSize * quads, 0.0f, quads);
        int cellX = Mathf.Min(Mathf.FloorToInt(gx), quads - 1);
        int cellY = Mathf.Min(Mathf.FloorToInt(gz), quads - 1);

        return LandscapeCellSurface.HeightIn(output, cellX, cellY, gx - cellX, gz - cellY);
    }
}
