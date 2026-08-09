using Godot;

namespace WorldMapStudio;

/// <summary>
/// Covers the geometry that keeps terrain at the edge of view correct: whatever overlaps a built
/// chunk has to be loaded, even when it sits outside the view.
///
/// The failure this guards is quiet and confusing — an edge chunk built from whichever of its
/// deformers happened to be loaded, which then changes shape as you fly toward it.
/// </summary>
public static class StreamingRegionTests
{
    private const float ChunkSize = 64.0f;

    private static LandscapeGrid Grid() =>
        new(new LandscapeSettings { ChunkWorldSize = ChunkSize, ChunkLimit = 1024 });

    private static Aabb Horizontal(Vector3 centre, float halfExtent) =>
        new(centre - new Vector3(halfExtent, 4096.0f, halfExtent),
            new Vector3(halfExtent * 2.0f, 8192.0f, halfExtent * 2.0f));

    [EditorTest(Category = "Streaming", Thread = TestThread.Background)]
    public static void The_load_margin_covers_every_chunk_the_view_builds()
    {
        // A chunk only partly inside the view is still built whole, so its far side reaches beyond
        // the view — and anything overlapping that far side must be loaded.
        LandscapeGrid grid = Grid();
        Aabb view = Horizontal(new Vector3(10.0f, 0.0f, 10.0f), 160.0f);
        Aabb load = Horizontal(new Vector3(10.0f, 0.0f, 10.0f), 160.0f + ChunkSize);

        foreach (ChunkCoord coord in grid.Overlapping(view))
        {
            Aabb chunk = grid.BoundsOf(coord);

            // A deformer sitting anywhere in this chunk must be inside the load region, or the chunk
            // is built without it.
            Assert.IsTrue(load.Encloses(Flatten(chunk)),
                $"chunk {coord} reaches outside the loaded region");
        }
    }

    [EditorTest(Category = "Streaming", Thread = TestThread.Background)]
    public static void A_view_sized_load_region_is_not_enough()
    {
        // Why the margin exists at all: without it, the outermost chunks stick out of the region
        // their inputs were read from.
        LandscapeGrid grid = Grid();
        Aabb view = Horizontal(new Vector3(10.0f, 0.0f, 10.0f), 160.0f);

        bool anyReachesOut = false;
        foreach (ChunkCoord coord in grid.Overlapping(view))
        {
            anyReachesOut |= !view.Encloses(Flatten(grid.BoundsOf(coord)));
        }

        Assert.IsTrue(anyReachesOut, "an edge chunk should extend past the view it was chosen by");
    }

    [EditorTest(Category = "Streaming", Thread = TestThread.Background)]
    public static void Climbing_does_not_take_the_world_with_you()
    {
        // Terrain is addressed on the ground plane, so altitude must not decide what is loaded.
        Aabb view = Horizontal(new Vector3(0.0f, 900.0f, 0.0f), 160.0f);
        var onTheGround = new Aabb(new Vector3(-5.0f, -1.0f, -5.0f), new Vector3(10.0f, 2.0f, 10.0f));

        Assert.IsTrue(view.Intersects(onTheGround));
    }

    // The vertical extent is effectively unbounded, so containment is a horizontal question.
    private static Aabb Flatten(Aabb box) =>
        new(new Vector3(box.Position.X, -1.0f, box.Position.Z), new Vector3(box.Size.X, 2.0f, box.Size.Z));
}
