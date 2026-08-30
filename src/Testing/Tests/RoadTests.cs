using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Covers <see cref="RoadPath"/>'s pure maths (chain extraction, the spline, the coverage profile) and
/// the built-in road function's integration with the landscape builder through an ordinary
/// <see cref="ProceduralComponent"/>. The chunk-continuity and determinism tests mirror
/// <see cref="LandscapeBuilderTests"/>'s two keeper tests, applied to the channel-scatter path a road
/// rasterizes through instead of the disc it uses there.
/// </summary>
public static class RoadTests
{
    // ---- RoadPath: chain extraction and the spline ----

    [EditorTest(Category = "Road", Thread = TestThread.Background)]
    public static void Straight_chain_passes_through_every_vertex()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(4.0f, 0.0f, 0.0f));
        int c = network.AddVertex(new Vector3(8.0f, 0.0f, 0.0f));
        int d = network.AddVertex(new Vector3(12.0f, 0.0f, 0.0f));
        network.AddEdge(a, b);
        network.AddEdge(b, c);
        network.AddEdge(c, d);

        RoadPath path = RoadPath.Build(network, centreWidth: 4.0f, shoulderWidth: 2.0f, falloff: 0.3f);

        Assert.Greater(path.Segments.Count, 0);
        foreach (int id in new[] { a, b, c, d })
        {
            Vector3 position = network.Vertex(id)!.Position;
            Assert.AreApproximatelyEqual(0.0, path.DistanceToPath(position), 0.05,
                $"the spline should pass through vertex {id}");
        }
    }

    [EditorTest(Category = "Road", Thread = TestThread.Background)]
    public static void Fork_keeps_every_branch_reachable()
    {
        var network = new VertexNetwork();
        int centre = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int a = network.AddVertex(new Vector3(10.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(-8.0f, 0.0f, 6.0f));
        int c = network.AddVertex(new Vector3(-4.0f, 0.0f, -9.0f));
        network.AddEdge(centre, a);
        network.AddEdge(centre, b);
        network.AddEdge(centre, c);

        RoadPath path = RoadPath.Build(network, centreWidth: 4.0f, shoulderWidth: 2.0f, falloff: 0.3f);

        foreach (int id in new[] { centre, a, b, c })
        {
            Vector3 position = network.Vertex(id)!.Position;
            Assert.AreApproximatelyEqual(0.0, path.DistanceToPath(position), 0.05,
                $"the spline should reach branch vertex {id}");
        }
    }

    [EditorTest(Category = "Road", Thread = TestThread.Background)]
    public static void Closed_loop_walks_every_edge_exactly_once()
    {
        // An equilateral triangle, side length 10: every vertex has degree 2, so there is no junction
        // at all and the whole thing must be recognised as one closed loop, not three dangling edges.
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(10.0f, 0.0f, 0.0f));
        int c = network.AddVertex(new Vector3(5.0f, 0.0f, 8.66f));
        network.AddEdge(a, b);
        network.AddEdge(b, c);
        network.AddEdge(c, a);

        RoadPath path = RoadPath.Build(network, centreWidth: 2.0f, shoulderWidth: 0.0f, falloff: 0.3f);

        // 3 edges * ceil(10 / FlattenStep=2) = 15 flattened segments — only true if the loop was
        // walked as one closed chain covering all three edges, rather than dropping the one that
        // would have closed it.
        Assert.AreEqual(15, path.Segments.Count);
        foreach (int id in new[] { a, b, c })
        {
            Vector3 position = network.Vertex(id)!.Position;
            Assert.AreApproximatelyEqual(0.0, path.DistanceToPath(position), 0.05);
        }
    }

    [EditorTest(Category = "Road", Thread = TestThread.Background)]
    public static void Isolated_vertex_contributes_no_segments()
    {
        var network = new VertexNetwork();
        network.AddVertex(new Vector3(3.0f, 0.0f, 3.0f));

        RoadPath path = RoadPath.Build(network, centreWidth: 4.0f, shoulderWidth: 2.0f, falloff: 0.3f);

        Assert.AreEqual(0, path.Segments.Count);
    }

    [EditorTest(Category = "Road", Thread = TestThread.Background)]
    public static void Duplicated_network_produces_identical_segments_in_identical_order()
    {
        var network = new VertexNetwork();
        int centre = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int a = network.AddVertex(new Vector3(10.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(-8.0f, 0.0f, 6.0f));
        network.AddEdge(centre, a);
        network.AddEdge(centre, b);

        RoadPath first = RoadPath.Build(network, 4.0f, 2.0f, 0.3f);
        RoadPath second = RoadPath.Build(network.Clone(), 4.0f, 2.0f, 0.3f);

        Assert.AreEqual(first.Segments.Count, second.Segments.Count);
        for (int i = 0; i < first.Segments.Count; i++)
        {
            Assert.AreEqual(first.Segments[i], second.Segments[i], $"segment {i} differs between identical networks");
        }
    }

    // ---- Coverage profile ----

    [EditorTest(Category = "Road", Thread = TestThread.Background)]
    public static void Coverage_is_full_on_the_centreline_and_zero_past_the_outer_radius()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(-20.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(20.0f, 0.0f, 0.0f));
        network.AddEdge(a, b);

        RoadPath path = RoadPath.Build(network, centreWidth: 4.0f, shoulderWidth: 3.0f, falloff: 0.25f);

        var onCentre = new Vector3(0.0f, 0.0f, 0.0f);
        Assert.AreApproximatelyEqual(1.0, path.CentreCoverage(onCentre), 1e-4);
        Assert.AreApproximatelyEqual(1.0, path.ShoulderCoverage(onCentre), 1e-4);

        var beyondOuter = new Vector3(0.0f, 0.0f, path.OuterRadius + 1.0f);
        Assert.AreApproximatelyEqual(0.0, path.CentreCoverage(beyondOuter), 1e-4);
        Assert.AreApproximatelyEqual(0.0, path.ShoulderCoverage(beyondOuter), 1e-4);
    }

    [EditorTest(Category = "Road", Thread = TestThread.Background)]
    public static void Shoulder_is_covered_everywhere_the_centre_is()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(-20.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(20.0f, 0.0f, 0.0f));
        network.AddEdge(a, b);

        RoadPath path = RoadPath.Build(network, centreWidth: 4.0f, shoulderWidth: 3.0f, falloff: 0.25f);

        for (float offset = 0.0f; offset <= path.OuterRadius + 1.0f; offset += 0.1f)
        {
            var point = new Vector3(0.0f, 0.0f, offset);
            float centre = path.CentreCoverage(point);
            float shoulder = path.ShoulderCoverage(point);
            if (centre > 0.999f)
            {
                Assert.IsTrue(shoulder > 0.999f, $"shoulder should be fully covered at offset {offset} wherever centre is");
            }
        }
    }

    // ---- Landscape build integration ----

    private sealed class Fixture
    {
        public required LandscapeSettings Settings { get; init; }
        public required LandscapeCatalog Catalog { get; init; }
        public required LandscapeFunctions Functions { get; init; }
        public required LandscapeLayer CentreLayer { get; init; }
        public required LandscapeLayer ShoulderLayer { get; init; }
        public required LandscapeMaterial CentreMaterial { get; init; }
        public required LandscapeMaterial ShoulderMaterial { get; init; }

        public LandscapeBuilder Builder() => new(Settings, Catalog, Functions);
    }

    private const string CentreChannel = "road_centre";
    private const string ShoulderChannel = "road_shoulder";

    private static Fixture BuildFixture()
    {
        var functions = new LandscapeFunctions();
        functions.Discover(typeof(ChannelMaskAlpha).Assembly);

        var centreValues = new LandscapeParameterValues();
        centreValues.Set(ChannelMaskAlpha.Mask, CentreChannel);
        centreValues.Set(ChannelMaskAlpha.Threshold, 0.5f);
        centreValues.Set(ChannelMaskAlpha.Softness, 0.0f);

        var shoulderValues = new LandscapeParameterValues();
        shoulderValues.Set(ChannelMaskAlpha.Mask, ShoulderChannel);
        shoulderValues.Set(ChannelMaskAlpha.Threshold, 0.5f);
        shoulderValues.Set(ChannelMaskAlpha.Softness, 0.0f);

        var centreMaterial = new LandscapeMaterial
        {
            Name = "road_centre_mat",
            RecordId = 1,
            TexturePath = "res://road_centre.png",
            AlphaFunction = "builtin.alpha.channel_mask",
            AlphaParameters = centreValues.Serialize(),
        };
        var shoulderMaterial = new LandscapeMaterial
        {
            Name = "road_shoulder_mat",
            RecordId = 2,
            TexturePath = "res://road_shoulder.png",
            AlphaFunction = "builtin.alpha.channel_mask",
            AlphaParameters = shoulderValues.Serialize(),
        };
        var groundMaterial = new LandscapeMaterial
        {
            Name = "ground",
            RecordId = 3,
            TexturePath = "res://ground.png",
        };

        var baseLayer = new LandscapeLayer { Name = "ground", RecordId = 1, DrawOrder = 0, IsBase = true };
        var shoulderLayer = new LandscapeLayer { Name = "road_shoulder", RecordId = 2, DrawOrder = 1 };
        var centreLayer = new LandscapeLayer { Name = "road_centre", RecordId = 3, DrawOrder = 2 };

        var channelCentre = new LandscapeChannel { Name = CentreChannel, RecordId = 1, Resolution = 32 };
        var channelShoulder = new LandscapeChannel { Name = ShoulderChannel, RecordId = 2, Resolution = 32 };

        var settings = new LandscapeSettings
        {
            ChunkWorldSize = 64.0f,
            ChunkHeightResolution = 17,
            ChunkAlphaResolution = 32,
            TextureLimit = 4,
            FallbackMaterialId = 3,
        };

        return new Fixture
        {
            Settings = settings,
            Catalog = new LandscapeCatalog(
                [channelCentre, channelShoulder],
                [baseLayer, shoulderLayer, centreLayer],
                [centreMaterial, shoulderMaterial, groundMaterial],
                functions),
            Functions = functions,
            CentreLayer = centreLayer,
            ShoulderLayer = shoulderLayer,
            CentreMaterial = centreMaterial,
            ShoulderMaterial = shoulderMaterial,
        };
    }

    private static EditorContext NewContext(string name) =>
        new(new Node3D(), new Project { Name = name });

    // Mirrors how the editor actually wires a road: the network/channel model carries no layer or
    // material knowledge of its own, and a sibling LandscapeMaterialBindComponent on the same entity is
    // what claims the centre and shoulder layers — see ImageTests for the same pattern. The
    // road is now an ordinary ProceduralComponent bound to a model on the built-in road function, so
    // its published paint (what Rasterize/InfluenceBounds actually read) only exists once the entity's
    // representation has been built — hence CreateRepresentation below, unlike the old RoadComponent
    // which computed its RoadPath eagerly in its own setters.
    private static (ProceduralComponent Road, List<ILandscapeDeformer> Deformers) RoadAt(
        EditorContext context, Fixture fixture, Transform3D transform, Vector3 localA, Vector3 localB, int modelId, int priority = 0)
    {
        var model = new ProceduralModel { RecordId = modelId, FunctionId = "builtin.procedural.road" };

        var values = new MeshParameterValues();
        values.Set(RoadNetworkFunction.CentreWidth, 8.0f);
        values.Set(RoadNetworkFunction.ShoulderWidth, 6.0f);
        values.Set(RoadNetworkFunction.Falloff, 0.3f);
        values.Set(RoadNetworkFunction.CentreChannel, CentreChannel);
        values.Set(RoadNetworkFunction.ShoulderChannel, ShoulderChannel);
        model.Parameters = values.Serialize();

        var network = new VertexNetwork();
        int a = network.AddVertex(localA);
        int b = network.AddVertex(localB);
        network.AddEdge(a, b);
        model.ReplaceNetwork(network);
        context.Catalog.Add(model);

        var road = new ProceduralComponent(context.Procedural) { ModelId = model.RecordId };

        var bind = new LandscapeMaterialBindComponent { Priority = priority };
        bind.ReplaceBindings(
        [
            new LandscapeMaterialBinding(fixture.CentreLayer.RecordId, fixture.CentreMaterial.RecordId),
            new LandscapeMaterialBinding(fixture.ShoulderLayer.RecordId, fixture.ShoulderMaterial.RecordId),
        ]);

        var entity = new SceneEntity();
        entity.AddComponent(road);
        entity.AddComponent(bind);
        entity.Transform = transform;

        context.Scene.Add(entity);
        entity.CreateRepresentation(context.Root);

        return (road, entity.Components.OfType<ILandscapeDeformer>().ToList());
    }

    [EditorTest(Category = "Road", Thread = TestThread.Main)]
    public static void Neighbouring_chunks_agree_on_the_shared_alpha_edge()
    {
        Fixture fixture = BuildFixture();
        EditorContext context = NewContext("__wms_road_edge_test__");

        // A road straddling the border between chunk (0,0) [x in 0..64) and (1,0) [x in 64..128),
        // running parallel to it so it clearly reaches the shared edge.
        (_, List<ILandscapeDeformer> deformers) = RoadAt(
            context,
            fixture,
            new Transform3D(Basis.Identity, new Vector3(64.0f, 0.0f, 32.0f)),
            new Vector3(-40.0f, 0.0f, 0.0f),
            new Vector3(40.0f, 0.0f, 0.0f),
            modelId: 1);

        LandscapeBuildResult result = fixture.Builder().Build([new ChunkCoord(0, 0), new ChunkCoord(1, 0)], deformers);

        LandscapeChunkOutput left = result.Chunks[new ChunkCoord(0, 0)];
        LandscapeChunkOutput right = result.Chunks[new ChunkCoord(1, 0)];
        int resolution = left.AlphaResolution;

        byte[] leftCentre = left.Layers.First(layer => layer.Material == fixture.CentreMaterial).Alpha!;
        byte[] rightCentre = right.Layers.First(layer => layer.Material == fixture.CentreMaterial).Alpha!;
        byte[] leftShoulder = left.Layers.First(layer => layer.Material == fixture.ShoulderMaterial).Alpha!;
        byte[] rightShoulder = right.Layers.First(layer => layer.Material == fixture.ShoulderMaterial).Alpha!;

        bool sawCoverage = false;
        for (int y = 0; y < resolution; y++)
        {
            byte leftEdgeCentre = leftCentre[(y * resolution) + (resolution - 1)];
            byte rightEdgeCentre = rightCentre[(y * resolution) + 0];
            Assert.AreEqual(leftEdgeCentre, rightEdgeCentre, $"centre alpha disagrees at shared edge row {y}");

            byte leftEdgeShoulder = leftShoulder[(y * resolution) + (resolution - 1)];
            byte rightEdgeShoulder = rightShoulder[(y * resolution) + 0];
            Assert.AreEqual(leftEdgeShoulder, rightEdgeShoulder, $"shoulder alpha disagrees at shared edge row {y}");

            sawCoverage |= leftEdgeCentre > 0;
        }

        Assert.IsTrue(sawCoverage, "the road must actually reach the shared edge for this to prove anything");
    }

    [EditorTest(Category = "Road", Thread = TestThread.Main)]
    public static void Building_the_same_block_twice_gives_identical_bytes()
    {
        // Guards against any dependence on iteration order or leftover pool state, same as the
        // builder-level version of this test — applied here to the per-segment scatter path a road
        // rasterizes through instead of a whole-chunk scan.
        Fixture fixture = BuildFixture();
        EditorContext context = NewContext("__wms_road_determinism_test__");
        List<ChunkCoord> block = [new(0, 0), new(1, 0), new(0, 1), new(1, 1)];

        (_, List<ILandscapeDeformer> deformersA) = RoadAt(
            context,
            fixture,
            new Transform3D(Basis.Identity, new Vector3(32.0f, 0.0f, 32.0f)),
            new Vector3(-20.0f, 0.0f, -10.0f),
            new Vector3(20.0f, 0.0f, 10.0f),
            modelId: 1);
        (_, List<ILandscapeDeformer> deformersB) = RoadAt(
            context,
            fixture,
            new Transform3D(Basis.Identity, new Vector3(96.0f, 0.0f, 96.0f)),
            new Vector3(-15.0f, 0.0f, 5.0f),
            new Vector3(15.0f, 0.0f, -5.0f),
            modelId: 2,
            priority: 1);
        List<ILandscapeDeformer> deformers = deformersA.Concat(deformersB).ToList();

        string first = Describe(fixture.Builder().Build(block, deformers));

        LandscapeBuilder reused = fixture.Builder();
        Assert.AreEqual(first, Describe(reused.Build(block, deformers)));
        Assert.AreEqual(first, Describe(reused.Build(block, deformers)), "the channel pool must reset between builds");
        Assert.AreEqual(first, Describe(fixture.Builder().Build(block, deformers.AsEnumerable().Reverse().ToList())));
    }

    [EditorTest(Category = "Road", Thread = TestThread.Main)]
    public static void Road_claims_nothing_itself()
    {
        Fixture fixture = BuildFixture();
        EditorContext editorContext = NewContext("__wms_road_claim_test__");
        (ProceduralComponent road, _) = RoadAt(
            editorContext,
            fixture,
            new Transform3D(Basis.Identity, new Vector3(32.0f, 0.0f, 32.0f)),
            new Vector3(-5.0f, 0.0f, 0.0f),
            new Vector3(5.0f, 0.0f, 0.0f),
            modelId: 1);

        var claimContext = new LandscapeClaimContext(new ChunkCoord(0, 0), new LandscapeGrid(fixture.Settings), fixture.Catalog);

        Assert.AreEqual(0, road.Claim(claimContext).Count(),
            "a road only writes channels — claiming the layer/material is LandscapeMaterialBindComponent's job");
    }

    [EditorTest(Category = "Road", Thread = TestThread.Main)]
    public static void Content_version_moves_for_shape_and_width_and_channel_but_not_for_position()
    {
        Fixture fixture = BuildFixture();
        EditorContext context = NewContext("__wms_road_contentversion_test__");
        (ProceduralComponent road, _) = RoadAt(
            context,
            fixture,
            new Transform3D(Basis.Identity, new Vector3(32.0f, 0.0f, 32.0f)),
            new Vector3(-5.0f, 0.0f, 0.0f),
            new Vector3(5.0f, 0.0f, 0.0f),
            modelId: 1);

        ProceduralModel model = road.Model!;
        int original = road.ContentVersion;

        road.Owner!.Transform = new Transform3D(Basis.Identity, new Vector3(50.0f, 0.0f, 50.0f));
        Assert.AreEqual(original, road.ContentVersion,
            "moving the entity must not change ContentVersion — InfluenceBounds is compared separately");

        MeshParameterValues values = MeshParameterValues.Parse(model.Parameters);
        values.Set(RoadNetworkFunction.CentreWidth, values.GetFloat(RoadNetworkFunction.CentreWidth) + 1.0f);
        model.Parameters = values.Serialize();
        int afterWidth = road.ContentVersion;
        Assert.AreNotEqual(original, afterWidth, "a width change must be visible");

        values.Set(RoadNetworkFunction.CentreChannel, "some_other_channel");
        model.Parameters = values.Serialize();
        int afterChannel = road.ContentVersion;
        Assert.AreNotEqual(afterWidth, afterChannel, "a channel rebind must be visible");

        VertexNetwork changed = model.Network.Clone();
        changed.MoveVertex(model.Network.Vertices[0].Id, new Vector3(-6.0f, 0.0f, 0.0f));
        model.ReplaceNetwork(changed);
        Assert.AreNotEqual(afterChannel, road.ContentVersion, "moving a vertex must be visible");
    }

    // A stable, comparable rendering of an entire build. Mirrors LandscapeBuilderTests.Describe.
    private static string Describe(LandscapeBuildResult result)
    {
        var parts = new List<string>();
        foreach (ChunkCoord coord in result.Chunks.Keys.OrderBy(c => c.Y).ThenBy(c => c.X))
        {
            LandscapeChunkOutput output = result.Chunks[coord];
            parts.Add($"{coord}:h[{string.Join(",", output.Heights.Select(h => h.ToString("F6")))}]");
            foreach (LandscapeChunkLayer layer in output.Layers)
            {
                parts.Add($"{layer.Material?.Name}:{(layer.Alpha == null ? "base" : System.Convert.ToBase64String(layer.Alpha))}");
            }
        }

        return string.Join("|", parts);
    }
}
