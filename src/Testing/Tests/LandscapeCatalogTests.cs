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
    public static void A_hole_only_material_is_valid()
    {
        // Same reasoning as the height-only case: which half a material needs is a property of the
        // layer it ends up bound to, not something checkable in the catalog alone.
        LandscapeCatalog catalog = Catalog(
            layers: [Layer("ground", 0, isBase: true)],
            materials: [new LandscapeMaterial { Name = "cave", HoleFunction = "test.hole" }]);

        Assert.AreEqual(0, catalog.Validate().Count(issue => issue.Severity == LandscapeIssueSeverity.Error));
    }

    [EditorTest(Category = "Landscape", Thread = TestThread.Background)]
    public static void A_material_that_does_nothing_at_all_is_flagged()
    {
        // A texture alone is enough to fill a base slot, so "does nothing" means neither half.
        LandscapeCatalog catalog = Catalog(
            layers: [Layer("ground", 0, isBase: true)],
            materials: [new LandscapeMaterial { Name = "empty" }]);

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

    [EditorTest(Category = "Landscape", Thread = TestThread.Background)]
    public static void Editing_a_material_in_place_changes_the_content_version()
    {
        // Nothing about the collection moves when a field is edited, so registry membership cannot
        // notice it — and a chunk would keep whatever it was built with until something else forced
        // a rebuild.
        var material = new LandscapeMaterial { Name = "dirt", RecordId = 1, AlphaFunction = "test" };
        LandscapeCatalog catalog = Catalog(layers: [Layer("ground", 0, isBase: true)], materials: [material]);

        int before = catalog.ContentVersion;

        material.HeightFunction = "test.height";
        Assert.AreNotEqual(before, catalog.ContentVersion, "binding a height function must be noticed");

        int afterFunction = catalog.ContentVersion;
        material.AlphaParameters = "{\"threshold\":\"0.8\"}";
        Assert.AreNotEqual(afterFunction, catalog.ContentVersion, "so must a parameter value");

        int afterAlphaParameters = catalog.ContentVersion;
        material.HoleFunction = "test.hole";
        Assert.AreNotEqual(afterAlphaParameters, catalog.ContentVersion, "binding a hole function must be noticed too");
    }

    [EditorTest(Category = "Landscape", Thread = TestThread.Background)]
    public static void Editing_a_layer_or_channel_in_place_changes_the_content_version()
    {
        var layer = Layer("ground", 0, isBase: true);
        var channel = new LandscapeChannel { Name = "mask", RecordId = 1 };
        LandscapeCatalog catalog = Catalog(layers: [layer], channels: [channel]);

        int before = catalog.ContentVersion;
        layer.DrawOrder = 5;
        Assert.AreNotEqual(before, catalog.ContentVersion);

        int afterOrder = catalog.ContentVersion;
        channel.Resolution = 128;
        Assert.AreNotEqual(afterOrder, catalog.ContentVersion);

        int afterResolution = catalog.ContentVersion;
        channel.Name = "renamed";
        Assert.AreNotEqual(afterResolution, catalog.ContentVersion, "channels are bound by name");
    }

    [EditorTest(Category = "Landscape", Thread = TestThread.Background)]
    public static void An_unchanged_catalog_keeps_its_content_version()
    {
        LandscapeCatalog catalog = Catalog(
            layers: [Layer("ground", 0, isBase: true)],
            materials: [new LandscapeMaterial { Name = "dirt", RecordId = 1, AlphaFunction = "test" }]);

        Assert.AreEqual(catalog.ContentVersion, catalog.ContentVersion, "reading it must not rebuild the world");
    }
}
