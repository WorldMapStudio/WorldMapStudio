using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Covers function discovery and the material bindings that resolve against it. The doubles here are
/// private nested types on purpose: the real scan takes public top-level types only, so they never
/// show up in the editor's own function list.
/// </summary>
public static class LandscapeFunctionTests
{
    private sealed class Fake : ILandscapeAlphaFunction
    {
        public static readonly LandscapeParameter Mask =
            LandscapeParameter.Channel("mask", "Mask", LandscapeChannelAccess.Read);

        public string Id => "test.fake";
        public string DisplayName => "Fake";
        public string Description => "";
        public int Version => 1;
        public float MaxSampleRadius => 12.0f;
        public IReadOnlyList<LandscapeParameter> Parameters { get; } = LandscapeParameter.List(Mask);
    }

    private sealed class DuplicateId : ILandscapeAlphaFunction
    {
        public string Id => "test.fake";
        public string DisplayName => "Duplicate";
        public string Description => "";
        public int Version => 1;
        public float MaxSampleRadius => 0.0f;
        public IReadOnlyList<LandscapeParameter> Parameters { get; } = [];
    }

    private sealed class NeedsAnArgument : ILandscapeHeightFunction
    {
        public NeedsAnArgument(int _) { }

        public string Id => "test.needs_argument";
        public string DisplayName => "Needs An Argument";
        public string Description => "";
        public int Version => 1;
        public float MaxSampleRadius => 0.0f;
        public IReadOnlyList<LandscapeParameter> Parameters { get; } = [];
    }

    private sealed class RepeatedParameter : ILandscapeAlphaFunction
    {
        public string Id => "test.repeated";
        public string DisplayName => "Repeated";
        public string Description => "";
        public int Version => 1;
        public float MaxSampleRadius => 0.0f;

        public IReadOnlyList<LandscapeParameter> Parameters { get; } = LandscapeParameter.List(
            LandscapeParameter.Float("amount", "Amount", 0.0f, 0.0f, 1.0f),
            LandscapeParameter.Float("amount", "Amount again", 0.0f, 0.0f, 1.0f));
    }

    private static LandscapeFunctions Registry(params Type[] types)
    {
        var functions = new LandscapeFunctions();
        functions.DiscoverFrom(types);
        return functions;
    }

    [EditorTest(Category = "LandscapeFunctions", Thread = TestThread.Background)]
    public static void The_builtin_functions_are_discovered()
    {
        var functions = new LandscapeFunctions();
        functions.Discover(typeof(ChannelMaskAlpha).Assembly);

        Assert.IsNotNull(functions.FindAlpha("builtin.alpha.channel_mask"));
        Assert.IsNotNull(functions.FindHeight("builtin.height.channel_flatten"));
        Assert.IsNull(functions.Find("test.fake"), "private nested doubles must not reach the real registry");
    }

    [EditorTest(Category = "LandscapeFunctions", Thread = TestThread.Background)]
    public static void An_alpha_function_is_not_a_height_function()
    {
        LandscapeFunctions functions = Registry(typeof(Fake));

        Assert.IsNotNull(functions.FindAlpha("test.fake"));
        Assert.IsNull(functions.FindHeight("test.fake"), "a material must not bind an alpha function as height");
    }

    [EditorTest(Category = "LandscapeFunctions", Thread = TestThread.Background)]
    public static void A_duplicate_id_is_refused_rather_than_silently_rebinding()
    {
        LandscapeFunctions functions = Registry(typeof(Fake), typeof(DuplicateId));

        Assert.AreEqual(1, functions.All.Count);
        Assert.AreEqual("Fake", functions.Find("test.fake")!.DisplayName);
        Assert.IsTrue(functions.Warnings.Any(w => w.Contains("already used")));
    }

    [EditorTest(Category = "LandscapeFunctions", Thread = TestThread.Background)]
    public static void Unusable_types_are_skipped_with_a_reason()
    {
        LandscapeFunctions functions = Registry(typeof(NeedsAnArgument), typeof(RepeatedParameter));

        Assert.AreEqual(0, functions.All.Count);
        Assert.IsTrue(functions.Warnings.Any(w => w.Contains("parameterless constructor")));
        Assert.IsTrue(functions.Warnings.Any(w => w.Contains("twice")));
    }

    [EditorTest(Category = "LandscapeFunctions", Thread = TestThread.Background)]
    public static void Parameter_values_round_trip_and_keep_unknown_keys()
    {
        var values = new LandscapeParameterValues();
        values.Set(ChannelMaskAlpha.Threshold, 0.75f);
        values.Set(ChannelMaskAlpha.Invert, true);
        values.Set(ChannelMaskAlpha.Mask, "road_mask");

        LandscapeParameterValues parsed = LandscapeParameterValues.Parse(values.Serialize());

        Assert.AreApproximatelyEqual(0.75, parsed.GetFloat(ChannelMaskAlpha.Threshold), 1e-5);
        Assert.IsTrue(parsed.GetBool(ChannelMaskAlpha.Invert));
        Assert.AreEqual("road_mask", parsed.GetChannel(ChannelMaskAlpha.Mask));

        // A value whose parameter a newer function version dropped must survive, not be destroyed by
        // opening the material in an editor that no longer knows about it.
        var retired = LandscapeParameter.Float("retired", "Retired", 0.0f, 0.0f, 1.0f);
        parsed.Set(retired, 3.0f);
        Assert.IsTrue(LandscapeParameterValues.Parse(parsed.Serialize()).Raw.ContainsKey("retired"));
    }

    [EditorTest(Category = "LandscapeFunctions", Thread = TestThread.Background)]
    public static void An_unset_parameter_reads_its_declared_default()
    {
        var empty = new LandscapeParameterValues();

        Assert.AreApproximatelyEqual(0.5, empty.GetFloat(ChannelMaskAlpha.Threshold), 1e-5);
        Assert.IsFalse(empty.GetBool(ChannelMaskAlpha.Invert));
    }

    [EditorTest(Category = "LandscapeFunctions", Thread = TestThread.Background)]
    public static void Unreadable_serialized_values_do_not_throw()
    {
        LandscapeParameterValues parsed = LandscapeParameterValues.Parse("{ this is not json");

        Assert.AreEqual(0, parsed.Raw.Count);
        Assert.AreApproximatelyEqual(0.5, parsed.GetFloat(ChannelMaskAlpha.Threshold), 1e-5);
    }

    [EditorTest(Category = "LandscapeFunctions", Thread = TestThread.Background)]
    public static void A_dangling_function_id_is_reported_but_the_binding_is_kept()
    {
        var material = new LandscapeTextureMaterial
        {
            Name = "dirt",
            TexturePath = "res://dirt.png",
            AlphaFunction = "plugin.not_loaded",
        };

        var catalog = new LandscapeCatalog([], [], [material], Registry(typeof(Fake)));

        Assert.IsTrue(catalog.Validate().Any(issue => issue.Message.Contains("which nothing provides")));
        Assert.AreEqual("plugin.not_loaded", material.AlphaFunction, "the binding survives so a reload restores it");
    }

    [EditorTest(Category = "LandscapeFunctions", Thread = TestThread.Background)]
    public static void Channel_bindings_must_name_a_channel_that_exists()
    {
        LandscapeFunctions functions = Registry(typeof(Fake));

        var values = new LandscapeParameterValues();
        values.Set(Fake.Mask, "missing_channel");
        var material = new LandscapeTextureMaterial
        {
            Name = "dirt",
            TexturePath = "res://dirt.png",
            AlphaFunction = "test.fake",
            AlphaParameters = values.Serialize(),
        };

        var channel = new LandscapeChannel { Name = "road_mask" };
        var catalog = new LandscapeCatalog([channel], [], [material], functions);
        Assert.IsTrue(catalog.Validate().Any(issue => issue.Message.Contains("does not exist")));

        values.Set(Fake.Mask, "road_mask");
        material.AlphaParameters = values.Serialize();
        var fixedCatalog = new LandscapeCatalog([channel], [], [material], functions);
        Assert.IsFalse(fixedCatalog.Validate().Any(issue => issue.Severity == LandscapeIssueSeverity.Error));
    }

    [EditorTest(Category = "LandscapeFunctions", Thread = TestThread.Background)]
    public static void An_unbound_channel_parameter_is_an_error()
    {
        var material = new LandscapeTextureMaterial
        {
            Name = "dirt",
            TexturePath = "res://dirt.png",
            AlphaFunction = "test.fake",
        };

        var catalog = new LandscapeCatalog([], [], [material], Registry(typeof(Fake)));

        Assert.IsTrue(catalog.Validate().Any(issue => issue.Message.Contains("unbound")));
    }

    [EditorTest(Category = "LandscapeFunctions", Thread = TestThread.Background)]
    public static void The_catalog_reports_the_largest_reach_in_use()
    {
        // The halo and the dirty set are sized from this, so it has to follow the bound functions
        // rather than whatever happens to be registered.
        LandscapeFunctions functions = Registry(typeof(Fake));

        var unbound = new LandscapeCatalog([], [], [], functions);
        Assert.AreApproximatelyEqual(0.0, unbound.MaxSampleRadius, 1e-5, "nothing bound means no halo is needed");

        var material = new LandscapeTextureMaterial { Name = "dirt", AlphaFunction = "test.fake" };
        var bound = new LandscapeCatalog([], [], [material], functions);
        Assert.AreApproximatelyEqual(12.0, bound.MaxSampleRadius, 1e-5);
    }
}
