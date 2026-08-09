using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Covers the slot resolver: the piece that decides what actually fits in a chunk. Pure data in,
/// pure data out, so all of it runs off the main thread with no database and no viewport.
/// </summary>
public static class LandscapeResolverTests
{
    private static LandscapeLayer Layer(string name, int order, bool isBase = false) =>
        new() { Name = name, DrawOrder = order, IsBase = isBase };

    private static LandscapeLayer HeightLayer(string name, int order) =>
        new() { Name = name, DrawOrder = order, Kind = LandscapeLayerKind.Height };

    private static LandscapeTextureMaterial Material(string name, int id) =>
        new() { Name = name, RecordId = id, TexturePath = $"res://{name}.png", AlphaFunction = "test" };

    private static LandscapeClaimGroup Group(
        string key,
        int priority,
        params (LandscapeLayer Layer, LandscapeTextureMaterial? Material)[] claims) =>
        new()
        {
            Key = key,
            Label = key,
            Priority = priority,
            Claims = claims.Select(c => new LandscapeClaim { Layer = c.Layer, Material = c.Material }).ToList(),
        };

    private static LandscapeResolution Resolve(
        IEnumerable<LandscapeClaimGroup> groups,
        int textureLimit,
        IEnumerable<LandscapeTextureMaterial>? materials = null,
        int? fallbackId = null)
    {
        var settings = new LandscapeSettings { TextureLimit = textureLimit, FallbackMaterialId = fallbackId };
        var catalog = new LandscapeCatalog([], [], (materials ?? []).ToList());
        return LandscapeResolver.Resolve(groups.ToList(), settings, catalog);
    }

    private static bool Has(LandscapeResolution resolution, LandscapeProblemKind kind) =>
        resolution.Problems.Any(problem => problem.Kind == kind);

    [EditorTest(Category = "LandscapeResolver", Thread = TestThread.Background)]
    public static void A_fitting_chunk_resolves_cleanly()
    {
        LandscapeLayer ground = Layer("ground", 0, isBase: true);
        LandscapeLayer road = Layer("road", 1);
        LandscapeTextureMaterial grass = Material("grass", 1);
        LandscapeTextureMaterial dirt = Material("dirt", 2);

        LandscapeResolution resolution = Resolve(
            [Group("terrain", 0, (ground, grass)), Group("road", 10, (road, dirt))],
            textureLimit: 4);

        Assert.IsTrue(resolution.IsClean);
        Assert.AreEqual(2, resolution.UsedSlots);
        Assert.AreEqual(grass, resolution.Base!.Material);
        Assert.AreEqual(0, resolution.Base.Index);
        Assert.AreEqual(1, resolution.AlphaSlots[0].Index);
        Assert.AreEqual(1, resolution.SlotOf(road));
    }

    [EditorTest(Category = "LandscapeResolver", Thread = TestThread.Background)]
    public static void One_layer_two_materials_is_a_conflict_the_priority_settles()
    {
        LandscapeLayer highlight = Layer("highlight", 1);
        LandscapeTextureMaterial grassA = Material("grass_a", 1);
        LandscapeTextureMaterial grassB = Material("grass_b", 2);

        LandscapeResolution resolution = Resolve(
            [Group("patch_a", 5, (highlight, grassA)), Group("patch_b", 9, (highlight, grassB))],
            textureLimit: 4);

        Assert.IsTrue(Has(resolution, LandscapeProblemKind.LayerConflict));
        Assert.AreEqual(grassB, resolution.AlphaSlots.Single().Material, "the higher priority claim wins");
        Assert.IsTrue(resolution.IsDropped("patch_a"));
    }

    [EditorTest(Category = "LandscapeResolver", Thread = TestThread.Background)]
    public static void Adjacent_layers_sharing_a_material_collapse_into_one_slot()
    {
        // road_dirt and ground_dirt both instantiated with dirt: one output slot serves both, which
        // is exactly the mitigation that buys a slot back without changing the picture.
        LandscapeLayer roadDirt = Layer("road_dirt", 2);
        LandscapeLayer groundDirt = Layer("ground_dirt", 3);
        LandscapeTextureMaterial dirt = Material("dirt", 1);

        LandscapeResolution resolution = Resolve(
            [Group("road", 0, (roadDirt, dirt)), Group("ground", 0, (groundDirt, dirt))],
            textureLimit: 4);

        LandscapeSlot merged = resolution.AlphaSlots.Single();
        Assert.IsTrue(merged.IsMerged);
        Assert.AreEqual(2, merged.Layers.Count);
        Assert.AreEqual(resolution.SlotOf(roadDirt), resolution.SlotOf(groundDirt));
    }

    [EditorTest(Category = "LandscapeResolver", Thread = TestThread.Background)]
    public static void Layers_separated_by_a_survivor_do_not_merge()
    {
        LandscapeLayer low = Layer("low", 1);
        LandscapeLayer middle = Layer("middle", 2);
        LandscapeLayer high = Layer("high", 3);
        LandscapeTextureMaterial dirt = Material("dirt", 1);
        LandscapeTextureMaterial stone = Material("stone", 2);

        LandscapeResolution resolution = Resolve(
            [Group("a", 0, (low, dirt)), Group("b", 0, (middle, stone)), Group("c", 0, (high, dirt))],
            textureLimit: 8);

        // Merging across the stone layer would reorder what draws over what.
        Assert.AreEqual(3, resolution.AlphaSlots.Count);
    }

    [EditorTest(Category = "LandscapeResolver", Thread = TestThread.Background)]
    public static void Dropping_a_layer_can_make_two_others_adjacent_and_mergeable()
    {
        // The case that forces merging to live *inside* the fit loop rather than running once up
        // front. Limit 2 = base + one alpha. Dropping stone makes the two dirt layers adjacent, so
        // they merge and both survive; merging only up front would have cost one of them.
        LandscapeLayer ground = Layer("ground", 0, isBase: true);
        LandscapeLayer lowDirt = Layer("low_dirt", 1);
        LandscapeLayer stoneLayer = Layer("stone", 2);
        LandscapeLayer highDirt = Layer("high_dirt", 3);
        LandscapeTextureMaterial grass = Material("grass", 1);
        LandscapeTextureMaterial dirt = Material("dirt", 2);
        LandscapeTextureMaterial stone = Material("stone", 3);

        LandscapeResolution resolution = Resolve(
        [
            Group("terrain", 100, (ground, grass)),
            Group("low", 50, (lowDirt, dirt)),
            Group("stone", 10, (stoneLayer, stone)),
            Group("high", 50, (highDirt, dirt)),
        ],
            textureLimit: 2);

        Assert.AreEqual(2, resolution.UsedSlots);
        Assert.IsTrue(resolution.IsDropped("stone"), "the lowest priority group is the one sacrificed");

        LandscapeSlot merged = resolution.AlphaSlots.Single();
        Assert.AreEqual(2, merged.Layers.Count, "both dirt layers survive by merging after the drop");
        Assert.IsTrue(merged.Layers.Contains(lowDirt));
        Assert.IsTrue(merged.Layers.Contains(highDirt));
    }

    [EditorTest(Category = "LandscapeResolver", Thread = TestThread.Background)]
    public static void A_base_never_merges_into_the_layer_above_it()
    {
        // The base is opaque and writes no alpha, so folding an alpha layer into it is not the same
        // picture even when the material matches.
        LandscapeLayer ground = Layer("ground", 0, isBase: true);
        LandscapeLayer patch = Layer("patch", 1);
        LandscapeTextureMaterial dirt = Material("dirt", 1);

        LandscapeResolution resolution = Resolve(
            [Group("terrain", 0, (ground, dirt)), Group("patch", 0, (patch, dirt))],
            textureLimit: 4);

        Assert.AreEqual(2, resolution.UsedSlots);
        Assert.IsFalse(resolution.Base!.IsMerged);
    }

    [EditorTest(Category = "LandscapeResolver", Thread = TestThread.Background)]
    public static void An_unclaimed_base_falls_back_so_the_chunk_is_not_a_hole()
    {
        LandscapeLayer road = Layer("road", 1);
        LandscapeTextureMaterial dirt = Material("dirt", 2);
        LandscapeTextureMaterial fallback = Material("fallback_grass", 7);

        LandscapeResolution resolution = Resolve(
            [Group("road", 0, (road, dirt))],
            textureLimit: 4,
            materials: [dirt, fallback],
            fallbackId: 7);

        Assert.IsTrue(resolution.IsClean);
        Assert.AreEqual(fallback, resolution.Base!.Material);
        Assert.AreEqual(0, resolution.Base.Layers.Count, "the fallback occupies the slot without a layer");
    }

    [EditorTest(Category = "LandscapeResolver", Thread = TestThread.Background)]
    public static void No_base_and_no_fallback_is_reported_once()
    {
        LandscapeLayer road = Layer("road", 1);

        LandscapeResolution resolution = Resolve([Group("road", 0, (road, Material("dirt", 2)))], textureLimit: 4);

        Assert.IsNull(resolution.Base);
        Assert.AreEqual(1, resolution.Problems.Count(p => p.Kind == LandscapeProblemKind.MissingBase),
            "the drop loop re-resolves, but a standing problem is reported once");
    }

    [EditorTest(Category = "LandscapeResolver", Thread = TestThread.Background)]
    public static void Only_one_base_may_be_bound()
    {
        LandscapeLayer rock = Layer("rock_base", 0, isBase: true);
        LandscapeLayer grassBase = Layer("grass_base", 1, isBase: true);

        LandscapeResolution resolution = Resolve(
            [Group("rock", 1, (rock, Material("rock", 1))), Group("grass", 9, (grassBase, Material("grass", 2)))],
            textureLimit: 4);

        Assert.IsTrue(Has(resolution, LandscapeProblemKind.MultipleBaseLayers));
        Assert.AreEqual("grass_base", resolution.Base!.Layers.Single().Name);
        Assert.IsTrue(resolution.IsDropped("rock"));
    }

    [EditorTest(Category = "LandscapeResolver", Thread = TestThread.Background)]
    public static void A_group_is_all_or_nothing()
    {
        // A road needing a centre and a shoulder takes both or neither: losing the centre to a
        // higher-priority claim must not leave a shoulder painted across the map on its own.
        LandscapeLayer centre = Layer("road_centre", 1);
        LandscapeLayer shoulder = Layer("road_shoulder", 2);
        LandscapeTextureMaterial asphalt = Material("asphalt", 1);
        LandscapeTextureMaterial gravel = Material("gravel", 2);
        LandscapeTextureMaterial cobble = Material("cobble", 3);

        LandscapeResolution resolution = Resolve(
        [
            Group("road", 5, (centre, asphalt), (shoulder, gravel)),
            Group("plaza", 9, (centre, cobble)),
        ],
            textureLimit: 8);

        Assert.IsTrue(resolution.IsDropped("road"));
        Assert.IsNull(resolution.SlotOf(shoulder), "the shoulder goes with the centre it was grouped to");
        Assert.AreEqual(cobble, resolution.AlphaSlots.Single().Material);
    }

    [EditorTest(Category = "LandscapeResolver", Thread = TestThread.Background)]
    public static void Height_layers_take_no_slot_and_run_in_draw_order()
    {
        LandscapeLayer ground = Layer("ground", 0, isBase: true);
        LandscapeLayer raise = HeightLayer("raise", 1);
        LandscapeLayer flatten = HeightLayer("flatten", 2);

        LandscapeResolution resolution = Resolve(
        [
            Group("terrain", 0, (ground, Material("grass", 1))),
            Group("road_bed", 0, (flatten, Material("road", 3))),
            Group("hill", 0, (raise, Material("hill", 2))),
        ],
            textureLimit: 1);

        Assert.IsTrue(resolution.IsClean, "height layers are not subject to the texture budget");
        Assert.AreEqual(1, resolution.UsedSlots);

        // Flatten must be able to run after raise, or the hill bumps through the road bed.
        Assert.AreEqual("raise", resolution.HeightClaims[0].Layer.Name);
        Assert.AreEqual("flatten", resolution.HeightClaims[1].Layer.Name);
    }

    [EditorTest(Category = "LandscapeResolver", Thread = TestThread.Background)]
    public static void Overflow_sacrifices_the_lowest_priority_group()
    {
        LandscapeLayer ground = Layer("ground", 0, isBase: true);
        LandscapeLayer important = Layer("road", 1);
        LandscapeLayer decorative = Layer("highlight", 2);

        LandscapeResolution resolution = Resolve(
        [
            Group("terrain", 100, (ground, Material("grass", 1))),
            Group("road", 50, (important, Material("dirt", 2))),
            Group("highlight", 1, (decorative, Material("moss", 3))),
        ],
            textureLimit: 2);

        Assert.IsTrue(Has(resolution, LandscapeProblemKind.BudgetOverflow));
        Assert.IsTrue(resolution.IsDropped("highlight"));
        Assert.IsNotNull(resolution.SlotOf(important));
    }

    [EditorTest(Category = "LandscapeResolver", Thread = TestThread.Background)]
    public static void A_texture_claim_without_a_material_is_reported_and_ignored()
    {
        LandscapeLayer ground = Layer("ground", 0, isBase: true);
        LandscapeLayer road = Layer("road", 1);

        LandscapeResolution resolution = Resolve(
        [
            Group("terrain", 0, (ground, Material("grass", 1))),
            Group("broken", 0, (road, null)),
        ],
            textureLimit: 4);

        Assert.IsTrue(Has(resolution, LandscapeProblemKind.MissingMaterial));
        Assert.AreEqual(1, resolution.UsedSlots);
        Assert.IsFalse(resolution.IsDropped("broken"), "only the unusable claim is ignored, not the group");
    }

    [EditorTest(Category = "LandscapeResolver", Thread = TestThread.Background)]
    public static void A_limit_that_cannot_be_met_is_reported_rather_than_looping()
    {
        LandscapeLayer ground = Layer("ground", 0, isBase: true);
        LandscapeTextureMaterial fallback = Material("fallback", 9);

        // Nothing is droppable once the base comes from the map's fallback rather than a claim.
        LandscapeResolution resolution = Resolve(
            [Group("terrain", 0, (ground, Material("grass", 1)))],
            textureLimit: 0,
            materials: [fallback],
            fallbackId: 9);

        Assert.IsTrue(Has(resolution, LandscapeProblemKind.Unsatisfiable));
    }

    [EditorTest(Category = "LandscapeResolver", Thread = TestThread.Background)]
    public static void The_outcome_does_not_depend_on_the_order_claims_arrive()
    {
        // Scan order is a database detail. If it leaked into the result, two machines could resolve
        // the same chunk differently and the terrain would not be reproducible.
        LandscapeLayer ground = Layer("ground", 0, isBase: true);
        LandscapeLayer a = Layer("a", 1);
        LandscapeLayer b = Layer("b", 2);
        LandscapeLayer c = Layer("c", 3);
        LandscapeTextureMaterial dirt = Material("dirt", 1);

        List<LandscapeClaimGroup> groups =
        [
            Group("terrain", 100, (ground, Material("grass", 9))),
            Group("alpha", 7, (a, dirt)),
            Group("beta", 7, (b, Material("stone", 2))),
            Group("gamma", 7, (c, dirt)),
        ];

        LandscapeResolution first = Resolve(groups, textureLimit: 3);

        var random = new Random(1234);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            List<LandscapeClaimGroup> shuffled = groups.OrderBy(_ => random.Next()).ToList();
            LandscapeResolution other = Resolve(shuffled, textureLimit: 3);

            Assert.AreEqual(Describe(first), Describe(other), $"shuffle {attempt} resolved differently");
        }
    }

    // A stable, comparable rendering of everything a resolution decided.
    private static string Describe(LandscapeResolution resolution) =>
        string.Join(" | ",
            string.Join(", ", resolution.Slots.Select(slot => slot.ToString())),
            string.Join(", ", resolution.HeightClaims.Select(claim => claim.Layer.Name)),
            string.Join(", ", resolution.DroppedGroups),
            string.Join(", ", resolution.Problems.Select(problem => problem.ToString())));
}
