using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Covers which regions an edit invalidates. Getting this wrong is quiet in the worst way: too much
/// and the editor just feels slow, too little and the terrain silently keeps a shape whose entity has
/// moved or gone.
/// </summary>
public static class LandscapeDirtyTests
{
    private sealed class Deformer : ILandscapeDeformer
    {
        public required string Key { get; init; }
        public Vector3 Centre { get; set; }
        public float Radius { get; set; } = 10.0f;
        public int Version { get; set; }

        public string DeformerKey => Key;

        public int ContentVersion => Version;

        public Aabb InfluenceBounds => new(
            Centre - new Vector3(Radius, Radius, Radius),
            new Vector3(Radius * 2.0f, Radius * 2.0f, Radius * 2.0f));

        public IEnumerable<LandscapeClaimGroup> Claim(in LandscapeClaimContext context) => [];

        public void Rasterize(in LandscapeRasterContext context) { }
    }

    private static bool Covers(IReadOnlyList<Aabb> regions, Vector3 point) =>
        regions.Any(region => region.HasPoint(point));

    [EditorTest(Category = "LandscapeDirty", Thread = TestThread.Background)]
    public static void A_new_deformer_dirties_where_it_is()
    {
        var tracker = new LandscapeDirtyTracker();
        var stamp = new Deformer { Key = "a", Centre = new Vector3(100.0f, 0.0f, 100.0f) };

        IReadOnlyList<Aabb> regions = tracker.Collect([stamp]);

        Assert.IsTrue(Covers(regions, stamp.Centre));
    }

    [EditorTest(Category = "LandscapeDirty", Thread = TestThread.Background)]
    public static void Nothing_changing_dirties_nothing()
    {
        var tracker = new LandscapeDirtyTracker();
        var stamp = new Deformer { Key = "a" };
        tracker.Collect([stamp]);

        Assert.AreEqual(0, tracker.Collect([stamp]).Count);
    }

    [EditorTest(Category = "LandscapeDirty", Thread = TestThread.Background)]
    public static void Moving_dirties_both_where_it_was_and_where_it_is()
    {
        // The one that matters. Dirtying only the destination leaves the terrain holding the shape
        // at the origin forever — a ghost of an entity that is no longer there.
        var tracker = new LandscapeDirtyTracker();
        var stamp = new Deformer { Key = "a", Centre = Vector3.Zero };
        tracker.Collect([stamp]);

        var from = new Vector3(0.0f, 0.0f, 0.0f);
        var to = new Vector3(500.0f, 0.0f, 0.0f);
        stamp.Centre = to;

        IReadOnlyList<Aabb> regions = tracker.Collect([stamp]);

        Assert.IsTrue(Covers(regions, from), "the vacated region must be rebuilt");
        Assert.IsTrue(Covers(regions, to), "and so must the new one");
    }

    [EditorTest(Category = "LandscapeDirty", Thread = TestThread.Background)]
    public static void Deleting_dirties_where_it_was()
    {
        var tracker = new LandscapeDirtyTracker();
        var stamp = new Deformer { Key = "a", Centre = new Vector3(70.0f, 0.0f, 0.0f) };
        tracker.Collect([stamp]);

        IReadOnlyList<Aabb> regions = tracker.Collect([]);

        Assert.IsTrue(Covers(regions, stamp.Centre));
        Assert.AreEqual(0, tracker.Count, "a deleted deformer stops being tracked");
    }

    [EditorTest(Category = "LandscapeDirty", Thread = TestThread.Background)]
    public static void A_parameter_edit_dirties_even_though_the_bounds_are_identical()
    {
        // Rebinding a channel or changing a falloff moves nothing, so bounds alone would report no
        // change and the edit would never appear.
        var tracker = new LandscapeDirtyTracker();
        var stamp = new Deformer { Key = "a", Centre = new Vector3(40.0f, 0.0f, 40.0f) };
        tracker.Collect([stamp]);

        stamp.Version = 7;
        IReadOnlyList<Aabb> regions = tracker.Collect([stamp]);

        Assert.IsTrue(Covers(regions, stamp.Centre));
    }

    [EditorTest(Category = "LandscapeDirty", Thread = TestThread.Background)]
    public static void Priming_records_without_reporting()
    {
        // Streaming has just built these chunks from this exact state, so the first frame must not
        // queue a rebuild of everything it already did.
        var tracker = new LandscapeDirtyTracker();
        var stamp = new Deformer { Key = "a" };

        tracker.Prime([stamp]);

        Assert.AreEqual(0, tracker.Collect([stamp]).Count);
    }

    [EditorTest(Category = "LandscapeDirty", Thread = TestThread.Background)]
    public static void Only_the_deformer_that_changed_is_reported()
    {
        var tracker = new LandscapeDirtyTracker();
        var moved = new Deformer { Key = "a", Centre = Vector3.Zero };
        var still = new Deformer { Key = "b", Centre = new Vector3(1000.0f, 0.0f, 0.0f) };
        tracker.Collect([moved, still]);

        moved.Centre = new Vector3(0.0f, 0.0f, 300.0f);
        IReadOnlyList<Aabb> regions = tracker.Collect([moved, still]);

        Assert.IsFalse(Covers(regions, still.Centre), "an untouched deformer's region stays clean");
        Assert.AreEqual(2, regions.Count, "just the vacated and the occupied region");
    }
}
