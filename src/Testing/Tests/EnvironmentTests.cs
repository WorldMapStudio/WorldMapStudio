using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>Covers <see cref="EnvironmentFalloff"/>, <see cref="EnvironmentValues.Blend"/> and
/// <see cref="EnvironmentBlender"/>, all pure logic that needs no scene or storage.</summary>
public static class EnvironmentTests
{
    private sealed class FakeSource : IEnvironmentSource
    {
        private readonly Vector3 _position;
        private readonly float _inner;
        private readonly float _outer;

        public FakeSource(EnvironmentValues values, bool isGlobal = false, Vector3 position = default, float inner = 0.0f, float outer = 0.0f)
        {
            Values = values;
            IsGlobal = isGlobal;
            _position = position;
            _inner = inner;
            _outer = outer;
        }

        public EnvironmentValues Values { get; }

        public bool IsGlobal { get; }

        public float WeightAt(Vector3 worldPosition) =>
            EnvironmentFalloff.Sphere(worldPosition.DistanceTo(_position), _inner, _outer);

        public EnvironmentValues Evaluate(in EnvironmentTime time) => Values;
    }

    [EditorTest(Category = "Environment")]
    public static void Falloff_is_full_inside_inner_radius() =>
        Assert.AreApproximatelyEqual(1.0, EnvironmentFalloff.Sphere(5.0f, 10.0f, 20.0f));

    [EditorTest(Category = "Environment")]
    public static void Falloff_is_zero_beyond_outer_radius() =>
        Assert.AreApproximatelyEqual(0.0, EnvironmentFalloff.Sphere(25.0f, 10.0f, 20.0f));

    [EditorTest(Category = "Environment")]
    public static void Falloff_interpolates_between_radii() =>
        Assert.AreApproximatelyEqual(0.5, EnvironmentFalloff.Sphere(15.0f, 10.0f, 20.0f));

    [EditorTest(Category = "Environment")]
    public static void Falloff_steps_when_inner_equals_outer()
    {
        Assert.AreApproximatelyEqual(1.0, EnvironmentFalloff.Sphere(10.0f, 10.0f, 10.0f));
        Assert.AreApproximatelyEqual(0.0, EnvironmentFalloff.Sphere(10.01f, 10.0f, 10.0f));
    }

    [EditorTest(Category = "Environment")]
    public static void Blend_at_zero_weight_returns_under()
    {
        var under = new EnvironmentValues { AmbientColor = Colors.Red };
        var over = new EnvironmentValues { AmbientColor = Colors.Blue };
        Assert.AreEqual(Colors.Red, EnvironmentValues.Blend(under, over, 0.0f).AmbientColor);
    }

    [EditorTest(Category = "Environment")]
    public static void Blend_at_full_weight_returns_over()
    {
        var under = new EnvironmentValues { AmbientColor = Colors.Red };
        var over = new EnvironmentValues { AmbientColor = Colors.Blue };
        Assert.AreEqual(Colors.Blue, EnvironmentValues.Blend(under, over, 1.0f).AmbientColor);
    }

    [EditorTest(Category = "Environment")]
    public static void Blend_lerps_scalars_and_colors()
    {
        var under = new EnvironmentValues { AmbientEnergy = 0.0f, FogEnd = 100.0f };
        var over = new EnvironmentValues { AmbientEnergy = 1.0f, FogEnd = 300.0f };
        EnvironmentValues blended = EnvironmentValues.Blend(under, over, 0.5f);

        Assert.AreApproximatelyEqual(0.5, blended.AmbientEnergy);
        Assert.AreApproximatelyEqual(200.0, blended.FogEnd);
    }

    [EditorTest(Category = "Environment")]
    public static void Blend_matches_gradient_stop_by_stop_when_counts_match()
    {
        var under = new EnvironmentValues
        {
            SkyGradient = [new SkyGradientStop(90.0f, Colors.Black), new SkyGradientStop(0.0f, Colors.Black)],
        };
        var over = new EnvironmentValues
        {
            SkyGradient = [new SkyGradientStop(90.0f, Colors.White), new SkyGradientStop(0.0f, Colors.White)],
        };

        IReadOnlyList<SkyGradientStop> blended = EnvironmentValues.Blend(under, over, 0.5f).SkyGradient;

        Assert.AreEqual(2, blended.Count);
        Assert.AreApproximatelyEqual(0.5, blended[0].Color.R);
        Assert.AreApproximatelyEqual(0.5, blended[1].Color.R);
    }

    [EditorTest(Category = "Environment")]
    public static void Blend_snaps_gradient_when_stop_counts_differ()
    {
        var under = new EnvironmentValues { SkyGradient = [new SkyGradientStop(0.0f, Colors.Black)] };
        var over = new EnvironmentValues
        {
            SkyGradient = [new SkyGradientStop(0.0f, Colors.White), new SkyGradientStop(90.0f, Colors.White)],
        };

        Assert.AreEqual(1, EnvironmentValues.Blend(under, over, 0.2f).SkyGradient.Count);
        Assert.AreEqual(2, EnvironmentValues.Blend(under, over, 0.8f).SkyGradient.Count);
    }

    [EditorTest(Category = "Environment")]
    public static void Blend_unions_sky_layers_by_path_and_scales_incoming_weight()
    {
        var under = new EnvironmentValues { SkyLayers = [new SkyLayer("res://stars.m2", 1.0f, 0.1f)] };
        var over = new EnvironmentValues { SkyLayers = [new SkyLayer("res://storm.m2", 1.0f, 0.1f)] };

        IReadOnlyList<SkyLayer> blended = EnvironmentValues.Blend(under, over, 0.4f).SkyLayers;

        Assert.AreEqual(2, blended.Count);
        foreach (SkyLayer layer in blended)
        {
            if (layer.ModelPath == "res://storm.m2")
            {
                Assert.AreApproximatelyEqual(0.4, layer.Weight);
            }
            else
            {
                Assert.AreApproximatelyEqual(1.0, layer.Weight);
            }
        }
    }

    [EditorTest(Category = "Environment")]
    public static void Blend_merges_named_floats_and_colors_present_on_only_one_side()
    {
        var under = new EnvironmentValues
        {
            Floats = new Dictionary<string, float> { ["wow.glow"] = 0.2f },
        };
        var over = new EnvironmentValues
        {
            Floats = new Dictionary<string, float> { ["wow.glow"] = 1.0f, ["wow.cloud"] = 0.6f },
        };

        IReadOnlyDictionary<string, float> blended = EnvironmentValues.Blend(under, over, 0.5f).Floats;

        Assert.AreApproximatelyEqual(0.6, blended["wow.glow"]);
        Assert.AreApproximatelyEqual(0.6, blended["wow.cloud"], message: "a key present on only one side should pass through unchanged");
    }

    [EditorTest(Category = "Environment")]
    public static void Blender_starts_from_the_global_source_at_full_weight()
    {
        var global = new FakeSource(new EnvironmentValues { AmbientColor = Colors.Red }, isGlobal: true);

        EnvironmentBlender.Result result = EnvironmentBlender.Blend([global], Vector3.Zero, default);

        Assert.AreEqual(Colors.Red, result.Current.AmbientColor);
        Assert.AreEqual(1, result.Active.Count);
        Assert.IsTrue(result.Active[0].Source.IsGlobal);
    }

    [EditorTest(Category = "Environment")]
    public static void Blender_folds_the_strongest_overlapping_source_in_first()
    {
        var global = new FakeSource(new EnvironmentValues { AmbientColor = Colors.Black }, isGlobal: true);

        // At the origin: strong's falloff steps to 1.0 (query point is inside its inner radius),
        // weak's gives 1 - 9/10 = 0.1. Composed strongest-first, strong all but overwrites the base
        // and weak only nudges it — so the result is ~0.9 red / ~0.1 blue, not the ~0.1/0.9 the
        // opposite order would leave.
        var strong = new FakeSource(new EnvironmentValues { AmbientColor = Colors.Red }, position: Vector3.Zero, inner: 10.0f, outer: 10.0f);
        var weak = new FakeSource(new EnvironmentValues { AmbientColor = Colors.Blue }, position: new Vector3(9.0f, 0.0f, 0.0f), inner: 0.0f, outer: 10.0f);

        EnvironmentBlender.Result result = EnvironmentBlender.Blend([global, weak, strong], Vector3.Zero, default);

        Assert.AreApproximatelyEqual(0.9, result.Current.AmbientColor.R, tolerance: 1e-4);
        Assert.AreApproximatelyEqual(0.1, result.Current.AmbientColor.B, tolerance: 1e-4);
        Assert.AreEqual(3, result.Active.Count);
        Assert.IsTrue(result.Active[1].Weight >= result.Active[2].Weight, "positional sources are ordered strongest-first");
    }

    [EditorTest(Category = "Environment")]
    public static void Blender_ignores_sources_with_no_weight_at_the_query_position()
    {
        var global = new FakeSource(new EnvironmentValues { AmbientColor = Colors.Black }, isGlobal: true);
        var farAway = new FakeSource(new EnvironmentValues { AmbientColor = Colors.Red }, position: new Vector3(1000.0f, 0.0f, 0.0f), inner: 1.0f, outer: 5.0f);

        EnvironmentBlender.Result result = EnvironmentBlender.Blend([global, farAway], Vector3.Zero, default);

        Assert.AreEqual(1, result.Active.Count);
        Assert.AreEqual(Colors.Black, result.Current.AmbientColor);
    }

    [EditorTest(Category = "Environment")]
    public static void Clock_day_fraction_wraps_around()
    {
        var clock = new WorldClock { DayFraction = 0.9f };
        clock.DayFraction += 0.2f;
        Assert.AreApproximatelyEqual(0.1, clock.DayFraction, tolerance: 1e-5);

        clock.DayFraction = -0.25f;
        Assert.AreApproximatelyEqual(0.75, clock.DayFraction, tolerance: 1e-5);
    }
}
