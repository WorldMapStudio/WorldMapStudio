using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Covers the rules the authored landscape has to satisfy before the resolver can use it. These are
/// pure data checks, so they run without a database or a viewport.
/// </summary>
public static class LandscapeCatalogTests
{
    private static LandscapeCatalog Catalog(
        IEnumerable<LandscapeLayer>? layers = null,
        IEnumerable<LandscapeChannel>? channels = null,
        IEnumerable<LandscapeMaterial>? materials = null) =>
        new(
            (channels ?? []).ToList(),
            (layers ?? []).ToList(),
            (materials ?? []).ToList());

    private static LandscapeLayer Layer(string name, int order, bool isBase = false) =>
        new() { Name = name, DrawOrder = order, IsBase = isBase };

    private static bool HasError(IReadOnlyList<LandscapeIssue> issues, string fragment) =>
        issues.Any(issue => issue.Severity == LandscapeIssueSeverity.Error && issue.Message.Contains(fragment));

    [EditorTest(Category = "Landscape", Thread = TestThread.Background)]
    public static void A_well_formed_catalog_has_no_errors()
    {
        LandscapeCatalog catalog = Catalog(
            layers: [Layer("ground", 0, isBase: true), Layer("road", 1)],
            channels: [new LandscapeChannel { Name = "road_mask" }],
            materials: [new LandscapeMaterial { Name = "dirt", AlphaFunction = "fn.mask", TexturePath = "res://dirt.png" }]);

        Assert.AreEqual(0, catalog.Validate().Count(issue => issue.Severity == LandscapeIssueSeverity.Error));
    }

    [EditorTest(Category = "Landscape", Thread = TestThread.Background)]
    public static void Draw_orders_must_be_unique()
    {
        // Merging identical materials depends on "adjacent in draw order" being well-defined, which
        // a shared order destroys.
        LandscapeCatalog catalog = Catalog(layers: [Layer("a", 2, isBase: true), Layer("b", 2)]);

        Assert.IsTrue(HasError(catalog.Validate(), "Draw order 2"));
    }

    [EditorTest(Category = "Landscape", Thread = TestThread.Background)]
    public static void Nothing_may_draw_below_the_base_layer()
    {
        // The base is opaque, so a layer ordered under it would be painted over and silently lost.
        LandscapeCatalog catalog = Catalog(layers: [Layer("detail", 0), Layer("ground", 1, isBase: true)]);

        Assert.IsTrue(HasError(catalog.Validate(), "draws below the base layer"));
    }

    [EditorTest(Category = "Landscape", Thread = TestThread.Background)]
    public static void A_height_layer_cannot_be_a_base()
    {
        LandscapeCatalog catalog = Catalog(layers:
        [
            new LandscapeLayer { Name = "flatten", DrawOrder = 0, Kind = LandscapeLayerKind.Height, IsBase = true },
        ]);

        Assert.IsTrue(HasError(catalog.Validate(), "only texture layers"));
    }

    [EditorTest(Category = "Landscape", Thread = TestThread.Background)]
    public static void Height_layers_are_ordered_with_texture_layers_but_take_no_slot()
    {
        var height = new LandscapeLayer { Name = "flatten", DrawOrder = 1, Kind = LandscapeLayerKind.Height };
        LandscapeCatalog catalog = Catalog(layers: [Layer("ground", 0, isBase: true), height, Layer("road", 2)]);

        Assert.IsFalse(height.UsesTextureSlot, "a height layer never consumes a texture slot");
        Assert.AreEqual(2, catalog.TextureLayersInOrder.Count());
        Assert.AreEqual(0, catalog.Validate().Count(issue => issue.Severity == LandscapeIssueSeverity.Error));
    }

    [EditorTest(Category = "Landscape", Thread = TestThread.Background)]
    public static void A_height_only_material_is_valid()
    {
        // Nothing here knows whether this material will be bound to a texture layer or a height one,
        // so demanding an alpha function would forbid the entirely reasonable height-only case.
        LandscapeCatalog catalog = Catalog(
            layers: [Layer("ground", 0, isBase: true)],
            materials: [new LandscapeMaterial { Name = "flatten", HeightFunction = "test.height" }]);

        Assert.AreEqual(0, catalog.Validate().Count(issue => issue.Severity == LandscapeIssueSeverity.Error));
    }

    [EditorTest(Category = "Landscape", Thread = TestThread.Background)]
    public static void A_material_that_does_nothing_at_all_is_flagged()
    {
        LandscapeCatalog catalog = Catalog(
            layers: [Layer("ground", 0, isBase: true)],
            materials: [new LandscapeMaterial { Name = "empty", TexturePath = "res://dirt.png" }]);

        Assert.IsTrue(catalog.Validate().Any(issue => issue.Message.Contains("does nothing")));
    }

    [EditorTest(Category = "Landscape", Thread = TestThread.Background)]
    public static void Duplicate_names_are_rejected_per_kind()
    {
        LandscapeCatalog catalog = Catalog(layers: [Layer("ground", 0, isBase: true), Layer("ground", 1)]);

        Assert.IsTrue(HasError(catalog.Validate(), "Two layers are named"));
    }

    [EditorTest(Category = "Landscape", Thread = TestThread.Background)]
    public static void Settings_reject_a_texture_limit_with_no_room_for_a_base()
    {
        var settings = new LandscapeSettings { TextureLimit = 0 };

        Assert.IsTrue(settings.Validate().Any(problem => problem.Contains("Texture limit")));
    }

    [EditorTest(Category = "Landscape", Thread = TestThread.Background)]
    public static void Changing_addressing_is_free_but_resolution_forces_a_rebuild()
    {
        var settings = new LandscapeSettings();

        LandscapeSettings moved = settings.Clone();
        moved.OriginChunkX = 32;
        moved.ChunkLimit = 128;
        Assert.AreEqual(LandscapeChangeCost.Free, LandscapeSettings.ChangeCost(settings, moved),
            "chunk coordinates are absolute, so addressing changes cost nothing");

        LandscapeSettings raised = settings.Clone();
        raised.TextureLimit = settings.TextureLimit + 1;
        Assert.AreEqual(LandscapeChangeCost.Free, LandscapeSettings.ChangeCost(settings, raised),
            "raising the budget cannot invalidate a chunk that already fit");

        LandscapeSettings lowered = settings.Clone();
        lowered.TextureLimit = 1;
        Assert.AreEqual(LandscapeChangeCost.Reresolve, LandscapeSettings.ChangeCost(settings, lowered));

        LandscapeSettings resized = settings.Clone();
        resized.ChunkAlphaResolution = 128;
        Assert.AreEqual(LandscapeChangeCost.Rebuild, LandscapeSettings.ChangeCost(settings, resized));
    }
}
