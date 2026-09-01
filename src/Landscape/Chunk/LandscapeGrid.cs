using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// A chunk's position on the landscape grid. Absolute, so moving the map's origin chunk or widening
/// its limit is pure addressing and costs nothing.
/// </summary>
public readonly record struct ChunkCoord(int X, int Y)
{
    /// <summary>
    /// The identity streaming deduplicates on. It has to include the map: every map has a chunk
    /// (0,0), and a key that ignored the map would make the newly entered map's chunk look like one
    /// already loaded — leaving the previous map's terrain on screen after switching.
    ///
    /// Coordinates are bounded by the map's chunk limit, so 20 bits each is far more room than any
    /// real map needs, and leaves 24 bits for the map id.
    /// </summary>
    public long KeyFor(MapId map) =>
        ((long)(map.Value & 0xFFFFFF) << 40) |
        ((long)(X & 0xFFFFF) << 20) |
        (uint)(Y & 0xFFFFF);

    /// <summary>Identity used for resolver tie-breaks and problem reporting. Must not depend on scan order.</summary>
    public override string ToString() => $"{X},{Y}";
}

/// <summary>
/// An explicit rectangle of chunks on one map — e.g. a user-picked range export target, as opposed to
/// whatever a <see cref="ChunkExportScope"/> resolves to.
/// </summary>
public readonly record struct ChunkRange(MapId Map, ChunkCoord Min, ChunkCoord Max)
{
    public IEnumerable<ChunkCoord> Coords()
    {
        for (int y = Min.Y; y <= Max.Y; y++)
        {
            for (int x = Min.X; x <= Max.X; x++)
            {
                yield return new ChunkCoord(x, y);
            }
        }
    }
}

/// <summary>
/// Converts between chunk coordinates and world space for one map's settings.
///
/// Chunks tile the world X/Z plane (Godot is Y-up, so Y is height and the grid's second axis is Z).
/// Streaming already works in Godot space rather than a separate map space, and the axis convention
/// stays a display concern, so the landscape follows the same rule.
/// </summary>
public readonly struct LandscapeGrid
{
    /// <summary>Vertical half-extent used for a chunk's bounds before real heights are known.</summary>
    public const float NominalHeightExtent = 512.0f;

    private readonly int _originX;
    private readonly int _originY;
    private readonly int _limit;

    public LandscapeGrid(LandscapeSettings settings)
    {
        ChunkSize = settings.ChunkWorldSize;
        _originX = settings.OriginChunkX;
        _originY = settings.OriginChunkY;
        _limit = settings.ChunkLimit;
    }

    /// <summary>Extent of one chunk in world units.</summary>
    public float ChunkSize { get; }

    /// <summary>World position of a chunk's minimum corner, at height zero.</summary>
    public Vector3 OriginOf(ChunkCoord coord) =>
        new((coord.X - _originX) * ChunkSize, 0.0f, (coord.Y - _originY) * ChunkSize);

    /// <summary>The chunk containing a world position.</summary>
    public ChunkCoord CoordAt(Vector3 world) => new(
        _originX + Mathf.FloorToInt(world.X / ChunkSize),
        _originY + Mathf.FloorToInt(world.Z / ChunkSize));

    /// <summary>A chunk's world bounds, using the nominal vertical extent until heights are built.</summary>
    public Aabb BoundsOf(ChunkCoord coord)
    {
        Vector3 origin = OriginOf(coord);
        return new Aabb(
            new Vector3(origin.X, -NominalHeightExtent, origin.Z),
            new Vector3(ChunkSize, NominalHeightExtent * 2.0f, ChunkSize));
    }

    /// <summary>Whether the coordinate is inside the map's chunk limit, measured from the origin chunk.</summary>
    public bool IsInLimits(ChunkCoord coord) =>
        Mathf.Abs(coord.X - _originX) <= _limit && Mathf.Abs(coord.Y - _originY) <= _limit;

    /// <summary>
    /// Every in-limit chunk whose footprint overlaps the region, in a stable order (Y then X) so two
    /// runs enumerate identically.
    /// </summary>
    public IEnumerable<ChunkCoord> Overlapping(Aabb region)
    {
        ChunkCoord min = CoordAt(region.Position);
        ChunkCoord max = CoordAt(region.End);

        for (int y = min.Y; y <= max.Y; y++)
        {
            for (int x = min.X; x <= max.X; x++)
            {
                var coord = new ChunkCoord(x, y);
                if (IsInLimits(coord))
                {
                    yield return coord;
                }
            }
        }
    }

    /// <summary>Chunks within <paramref name="radius"/> world units of the region — the builder's halo.</summary>
    public IEnumerable<ChunkCoord> OverlappingWithHalo(Aabb region, float radius)
    {
        var grown = new Aabb(
            region.Position - new Vector3(radius, 0.0f, radius),
            region.Size + new Vector3(radius * 2.0f, 0.0f, radius * 2.0f));
        return Overlapping(grown);
    }
}
