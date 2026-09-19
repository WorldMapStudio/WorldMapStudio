using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

public static class BrushTests
{
    private sealed class RecordingTarget(Func<Vector3, bool>? covers = null) : IStrokeTarget
    {
        public List<BrushDab> Dabs { get; } = [];
        public bool Began { get; private set; }
        public bool Canceled { get; private set; }

        public bool Covers(Vector3 world) => covers?.Invoke(world) ?? true;

        public bool Begin()
        {
            Began = true;
            return true;
        }

        public bool Dab(in BrushDab dab)
        {
            Dabs.Add(dab);
            return true;
        }

        public IEditCommand? Finish() => Dabs.Count == 0 ? null : new BatchEditCommand("Recorded", []);

        public void Cancel() => Canceled = true;
    }

    [EditorTest(Category = "Brush", Thread = TestThread.Background)]
    public static void Falloff_with_zero_hardness_matches_smoothstep_exactly()
    {
        for (int i = 0; i <= 1000; i++)
        {
            float t = i / 1000.0f;
            Assert.AreEqual(Mathf.SmoothStep(0.0f, 1.0f, 1.0f - t), BrushFalloff.Weight(t, 0.0f), $"t={t}");
        }

        Assert.AreEqual(0.0f, BrushFalloff.Weight(1.0001f, 0.0f));
    }

    [EditorTest(Category = "Brush", Thread = TestThread.Background)]
    public static void Falloff_hardness_holds_full_weight_to_the_inner_radius()
    {
        Assert.AreEqual(1.0f, BrushFalloff.Weight(0.0f, 0.5f));
        Assert.AreEqual(1.0f, BrushFalloff.Weight(0.5f, 0.5f));
        Assert.IsTrue(BrushFalloff.Weight(0.75f, 0.5f) < 1.0f);
        Assert.AreEqual(0.0f, BrushFalloff.Weight(1.0f, 0.5f));
        Assert.AreEqual(1.0f, BrushFalloff.Weight(1.0f, 1.0f), "a fully hard brush is a flat disc");
        Assert.AreEqual(0.0f, BrushFalloff.Weight(1.01f, 1.0f));
    }

    [EditorTest(Category = "Brush", Thread = TestThread.Background)]
    public static void Row_kernel_avx_matches_scalar_at_every_hardness()
    {
        foreach (float hardness in new[] { 0.0f, 0.3f, 0.75f, 1.0f })
        {
            float[] scalar = Row(hardness, forceScalar: true);
            float[] simd = Row(hardness, forceScalar: false);
            for (int i = 0; i < scalar.Length; i++)
            {
                Assert.IsTrue(MathF.Abs(scalar[i] - simd[i]) <= 1e-4f, $"hardness {hardness}, texel {i}");
            }

            Assert.IsTrue(scalar.Any(value => value > 0.0f), "the row should have been touched");
        }
    }

    [EditorTest(Category = "Brush", Thread = TestThread.Background)]
    public static void Row_kernel_with_zero_hardness_matches_the_plain_smoothstep_formula()
    {
        float[] simd = Row(0.0f, forceScalar: false);
        float[] expected = new float[simd.Length];
        for (int i = 0; i < expected.Length; i++)
        {
            float dx = ((i + 0.5f) / expected.Length * 2.0f) - 1.0f;
            float distSq = (dx * dx) + 0.09f;
            if (distSq <= 1.0f)
            {
                float s = 1.0f - MathF.Sqrt(distSq);
                expected[i] = 0.7f * (s * s * (3.0f - (2.0f * s)));
            }
        }

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.IsTrue(MathF.Abs(expected[i] - simd[i]) <= 1e-6f, $"texel {i}: {expected[i]} vs {simd[i]}");
        }
    }

    private static float[] Row(float hardness, bool forceScalar)
    {
        const int Count = 37;
        var values = new float[Count];
        var columnDistSq = new float[Count];
        for (int i = 0; i < Count; i++)
        {
            float dx = ((i + 0.5f) / Count * 2.0f) - 1.0f;
            columnDistSq[i] = dx * dx;
        }

        bool previous = BrushFalloff.ForceScalar;
        BrushFalloff.ForceScalar = forceScalar;
        try
        {
            bool changed = false;
            BrushFalloff.AddRow(values, columnDistSq, 0.09f, 0.7f, hardness, ref changed);
            Assert.IsTrue(changed);
        }
        finally
        {
            BrushFalloff.ForceScalar = previous;
        }

        return values;
    }

    [EditorTest(Category = "Brush", Thread = TestThread.Background)]
    public static void Stroke_spaces_dabs_by_travel()
    {
        var brush = new Brush { Radius = 4.0f, Spacing = 0.25f };
        var target = new RecordingTarget();
        var stroke = new BrushStroke(brush, target, new EditSessionManager());

        Assert.IsTrue(stroke.Begin());
        stroke.MoveTo(new Vector3(0, 0, 0));
        Assert.AreEqual(1, target.Dabs.Count, "the first dab lands where the stroke starts");

        stroke.MoveTo(new Vector3(10, 0, 0));
        Assert.AreEqual(11, target.Dabs.Count, "one dab per unit of the 10 travelled");

        stroke.MoveTo(new Vector3(10.4f, 0, 0));
        Assert.AreEqual(11, target.Dabs.Count, "the leftover distance carries to the next call");
        Assert.IsFalse(stroke.LaidDabs);

        stroke.MoveTo(new Vector3(11.2f, 0, 0));
        Assert.AreEqual(12, target.Dabs.Count);
    }

    [EditorTest(Category = "Brush", Thread = TestThread.Background)]
    public static void Stroke_drops_a_backlog_beyond_the_per_call_cap()
    {
        var brush = new Brush { Radius = 1.0f, Spacing = 0.25f };
        var target = new RecordingTarget();
        var stroke = new BrushStroke(brush, target, new EditSessionManager());
        stroke.Begin();
        stroke.MoveTo(Vector3.Zero);

        stroke.MoveTo(new Vector3(1_000_000, 0, 0));

        Assert.AreEqual(1 + 512, target.Dabs.Count);
        stroke.MoveTo(new Vector3(1_000_000.1f, 0, 0));
        Assert.AreEqual(1 + 512, target.Dabs.Count, "the backlog is gone, not queued");
    }

    [EditorTest(Category = "Brush", Thread = TestThread.Background)]
    public static void Airbrush_repeats_a_still_pointer_on_the_callers_clock()
    {
        var brush = new Brush { Radius = 4.0f, AirbrushRate = 50.0f };
        var target = new RecordingTarget();
        var stroke = new BrushStroke(brush, target, new EditSessionManager());
        stroke.Begin();
        Vector3 point = new(3, 0, 3);

        stroke.Advance(point, 0.000);
        Assert.AreEqual(1, target.Dabs.Count);

        stroke.Advance(point, 0.010);
        Assert.AreEqual(1, target.Dabs.Count, "10 ms is inside the 20 ms interval");

        stroke.Advance(point, 0.021);
        Assert.AreEqual(2, target.Dabs.Count);

        stroke.Advance(point, 0.030);
        Assert.AreEqual(2, target.Dabs.Count, "the interval restarts at the last dab");

        stroke.Advance(point, 0.042);
        Assert.AreEqual(3, target.Dabs.Count);
    }

    [EditorTest(Category = "Brush", Thread = TestThread.Background)]
    public static void Points_the_target_does_not_cover_are_skipped()
    {
        var brush = new Brush { Radius = 2.0f };
        var target = new RecordingTarget(world => world.X >= 0.0f);
        var stroke = new BrushStroke(brush, target, new EditSessionManager());
        stroke.Begin();

        Assert.IsFalse(stroke.MoveTo(new Vector3(-5, 0, 0)));
        Assert.AreEqual(0, target.Dabs.Count);

        stroke.MoveTo(new Vector3(1, 0, 0));
        Assert.AreEqual(1, target.Dabs.Count);
    }

    [EditorTest(Category = "Brush", Thread = TestThread.Background)]
    public static void Spray_scatters_each_dab_into_small_dabs_inside_the_brush()
    {
        var brush = new Brush { Radius = 8.0f, Spray = true, SprayCount = 6, Hardness = 0.4f, Strength = 0.5f };
        var target = new RecordingTarget();
        var stroke = new BrushStroke(brush, target, new EditSessionManager(), new Random(7));
        stroke.Begin();

        stroke.MoveTo(new Vector3(20, 0, 20));

        Assert.AreEqual(6, target.Dabs.Count);
        foreach (BrushDab dab in target.Dabs)
        {
            Assert.IsTrue(dab.Radius < brush.Radius);
            Assert.IsTrue(new Vector2(dab.Center.X - 20, dab.Center.Z - 20).Length() + dab.Radius <= brush.Radius + 1e-3f);
            Assert.AreEqual(0.4f, dab.Hardness);
            Assert.AreEqual(0.5f, dab.Strength);
        }
    }

    [EditorTest(Category = "Brush", Thread = TestThread.Background)]
    public static void Invert_flips_per_stroke_without_changing_the_brush()
    {
        var brush = new Brush { Invert = false };
        var target = new RecordingTarget();
        var stroke = new BrushStroke(brush, target, new EditSessionManager());
        stroke.Begin(invert: true);
        stroke.MoveTo(Vector3.Zero);

        Assert.IsTrue(target.Dabs[0].Invert);
        Assert.IsFalse(brush.Invert);
    }

    [EditorTest(Category = "Brush", Thread = TestThread.Background)]
    public static void Finish_records_the_targets_command_only_when_asked()
    {
        var sessions = new EditSessionManager();
        var target = new RecordingTarget();
        var stroke = new BrushStroke(new Brush(), target, sessions);

        stroke.Begin();
        stroke.MoveTo(Vector3.Zero);
        Assert.IsTrue(stroke.Finish(record: true));
        Assert.IsFalse(stroke.IsActive);
        Assert.IsTrue(sessions.Active.History.CanUndo);

        var abandoned = new RecordingTarget();
        var second = new BrushStroke(new Brush(), abandoned, sessions);
        second.Begin();
        second.MoveTo(Vector3.Zero);
        Assert.IsFalse(second.Finish(record: false));
        Assert.AreEqual(1, sessions.Active.History.UndoStack.Count);
        Assert.IsTrue(abandoned.Canceled);

        Assert.Throws<InvalidOperationException>(() => stroke.MoveTo(Vector3.Zero));
    }

    [EditorTest(Category = "Brush", Thread = TestThread.Background)]
    public static void Raster_over_two_grids_matches_one_unbroken_grid()
    {
        var dab = new BrushDab(new Vector3(8.3f, 0, 5.1f), 6.0f, 1.0f, false, 0.25f);

        double whole = Sum(new BrushGrid(0, 0, 0.5f, 0.5f, 32, 20), dab);
        double left = Sum(new BrushGrid(0, 0, 0.5f, 0.5f, 16, 20), dab);
        double right = Sum(new BrushGrid(8, 0, 0.5f, 0.5f, 16, 20), dab);

        Assert.AreApproximatelyEqual(whole, left + right, 1e-3);
        Assert.IsTrue(whole > 0.0);
    }

    [EditorTest(Category = "Brush", Thread = TestThread.Background)]
    public static void Raster_skips_a_grid_the_dab_does_not_reach()
    {
        var grid = new BrushGrid(100, 100, 1, 1, 8, 8);
        var dab = new BrushDab(Vector3.Zero, 5.0f, 1.0f, false, 0.0f);

        Assert.IsFalse(BrushRaster.Overlaps(grid, dab));
        Assert.AreEqual(0, BrushRaster.Walk(grid, dab, (_, _, _) => Assert.Fail("no texel should be visited")));
    }

    private static double Sum(in BrushGrid grid, in BrushDab dab)
    {
        double total = 0.0;
        BrushRaster.Walk(grid, dab, (_, _, weight) => total += weight);
        return total;
    }
}
