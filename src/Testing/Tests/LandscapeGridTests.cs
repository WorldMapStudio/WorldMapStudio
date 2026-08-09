using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Covers chunk addressing. Getting this wrong is subtle and expensive — an off-by-one at a negative
/// coordinate shows up as a row of chunks that never load, a long way from where the bug lives.
/// </summary>
public static class LandscapeGridTests
{
    private static LandscapeGrid Grid(float size = 64.0f, int originX = 0, int originY = 0, int limit = 64) =>
        new(new LandscapeSettings
        {
            ChunkWorldSize = size,
            OriginChunkX = originX,
            OriginChunkY = originY,
            ChunkLimit = limit,
        });

    [EditorTest(Category = "LandscapeGrid", Thread = TestThread.Background)]
    public static void World_and_chunk_coordinates_round_trip()
    {
        LandscapeGrid grid = Grid();

        Assert.AreEqual(new ChunkCoord(0, 0), grid.CoordAt(new Vector3(0.0f, 0.0f, 0.0f)));
        Assert.AreEqual(new ChunkCoord(0, 0), grid.CoordAt(new Vector3(63.9f, 0.0f, 63.9f)));
        Assert.AreEqual(new ChunkCoord(1, 2), grid.CoordAt(new Vector3(64.0f, 0.0f, 128.0f)));

        Vector3 origin = grid.OriginOf(new ChunkCoord(1, 2));
        Assert.AreApproximatelyEqual(64.0, origin.X, 1e-4);
        Assert.AreApproximatelyEqual(128.0, origin.Z, 1e-4);
        Assert.AreEqual(new ChunkCoord(1, 2), grid.CoordAt(origin));
    }

    [EditorTest(Category = "LandscapeGrid", Thread = TestThread.Background)]
    public static void Negative_positions_floor_rather_than_truncate()
    {
        // Truncation would map both -0.5 and +0.5 to chunk 0, so the chunk left of the origin would
        // be half the width of every other chunk.
        LandscapeGrid grid = Grid();

        Assert.AreEqual(new ChunkCoord(-1, -1), grid.CoordAt(new Vector3(-0.5f, 0.0f, -0.5f)));
        Assert.AreEqual(new ChunkCoord(-1, -1), grid.CoordAt(new Vector3(-64.0f, 0.0f, -64.0f)));
        Assert.AreEqual(new ChunkCoord(-2, -2), grid.CoordAt(new Vector3(-64.5f, 0.0f, -64.5f)));
    }

    [EditorTest(Category = "LandscapeGrid", Thread = TestThread.Background)]
    public static void The_origin_chunk_shifts_addressing_without_moving_the_world()
    {
        LandscapeGrid grid = Grid(originX: 32, originY: 32);

        Assert.AreEqual(new ChunkCoord(32, 32), grid.CoordAt(Vector3.Zero));

        Vector3 origin = grid.OriginOf(new ChunkCoord(32, 32));
        Assert.AreApproximatelyEqual(0.0, origin.X, 1e-4);
        Assert.AreApproximatelyEqual(0.0, origin.Z, 1e-4);
    }

    [EditorTest(Category = "LandscapeGrid", Thread = TestThread.Background)]
    public static void Overlapping_covers_every_chunk_a_region_touches()
    {
        LandscapeGrid grid = Grid();
        var region = new Aabb(new Vector3(-10.0f, 0.0f, -10.0f), new Vector3(100.0f, 1.0f, 100.0f));

        List<ChunkCoord> coords = grid.Overlapping(region).ToList();

        // Spans x in [-1, 1] and y in [-1, 1]: nine chunks.
        Assert.AreEqual(9, coords.Count);
        Assert.IsTrue(coords.Contains(new ChunkCoord(-1, -1)));
        Assert.IsTrue(coords.Contains(new ChunkCoord(1, 1)));
    }

    [EditorTest(Category = "LandscapeGrid", Thread = TestThread.Background)]
    public static void Enumeration_order_is_stable()
    {
        LandscapeGrid grid = Grid();
        var region = new Aabb(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(128.0f, 1.0f, 128.0f));

        string first = string.Join(" ", grid.Overlapping(region));
        string second = string.Join(" ", grid.Overlapping(region));

        Assert.AreEqual(first, second);
        Assert.AreEqual("0,0 1,0 2,0 0,1 1,1 2,1 0,2 1,2 2,2", first);
    }

    [EditorTest(Category = "LandscapeGrid", Thread = TestThread.Background)]
    public static void Chunks_beyond_the_limit_are_not_enumerated()
    {
        LandscapeGrid grid = Grid(limit: 1);
        var region = new Aabb(new Vector3(-200.0f, 0.0f, -200.0f), new Vector3(400.0f, 1.0f, 400.0f));

        List<ChunkCoord> coords = grid.Overlapping(region).ToList();

        Assert.AreEqual(9, coords.Count, "a limit of 1 allows coordinates -1..1 on each axis");
        Assert.IsFalse(coords.Contains(new ChunkCoord(2, 0)));
        Assert.IsFalse(grid.IsInLimits(new ChunkCoord(0, -2)));
    }

    [EditorTest(Category = "LandscapeGrid", Thread = TestThread.Background)]
    public static void The_halo_widens_the_region_by_the_sample_radius()
    {
        // The halo is what a function's declared reach buys: chunks that are not themselves built,
        // but whose channels the built ones may sample.
        LandscapeGrid grid = Grid();
        var region = new Aabb(new Vector3(10.0f, 0.0f, 10.0f), new Vector3(10.0f, 1.0f, 10.0f));

        Assert.AreEqual(1, grid.Overlapping(region).Count());
        Assert.AreEqual(9, grid.OverlappingWithHalo(region, 64.0f).Count());
    }

    [EditorTest(Category = "LandscapeGrid", Thread = TestThread.Background)]
    public static void Stream_keys_are_unique_across_coordinates_and_maps()
    {
        // Streaming deduplicates on this key. A collision between coordinates would silently drop a
        // chunk; a collision between maps would leave the previous map's terrain on screen after
        // switching, because the new chunk would look like one already loaded.
        var keys = new HashSet<long>();
        foreach (int map in new[] { 0, 1, 530 })
        {
            for (int x = -8; x <= 8; x++)
            {
                for (int y = -8; y <= 8; y++)
                {
                    long key = new ChunkCoord(x, y).KeyFor(new MapId(map));
                    Assert.IsTrue(keys.Add(key), $"key collision at map {map}, chunk {x},{y}");
                }
            }
        }
    }

    [EditorTest(Category = "LandscapeGrid", Thread = TestThread.Background)]
    public static void A_chunks_bounds_sit_where_its_origin_does()
    {
        LandscapeGrid grid = Grid();
        Aabb bounds = grid.BoundsOf(new ChunkCoord(2, 3));

        Assert.AreApproximatelyEqual(128.0, bounds.Position.X, 1e-4);
        Assert.AreApproximatelyEqual(192.0, bounds.Position.Z, 1e-4);
        Assert.AreApproximatelyEqual(64.0, bounds.Size.X, 1e-4);
        Assert.AreApproximatelyEqual(64.0, bounds.Size.Z, 1e-4);
    }
}
