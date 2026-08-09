using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Covers the builder end to end. Two of these — determinism and edge continuity — are the tests the
/// plan says to keep forever: both failures are subtle, visual, and expensive to debug from the
/// symptom, and both are cheap to catch here.
/// </summary>
public static class LandscapeBuilderTests
{
    private const string MaskChannel = "mask";

    // A deformer that writes a fixed value inside a world-space radius, without depending on which
    // chunk it is being rasterized into.
    private sealed class Disc : ILandscapeDeformer
    {
        public required string Key { get; init; }
        public required Vector3 Centre { get; init; }
        public required float Radius { get; init; }
        public required LandscapeLayer TextureLayer { get; init; }
        public LandscapeLayer? HeightLayer { get; init; }
        public required LandscapeMaterial Material { get; init; }
        public int ClaimPriority { get; init; }

        public string DeformerKey => Key;

        public Aabb InfluenceBounds => new(
            Centre - new Vector3(Radius, Radius, Radius),
            new Vector3(Radius * 2.0f, Radius * 2.0f, Radius * 2.0f));

        public IEnumerable<LandscapeClaimGroup> Claim(in LandscapeClaimContext context)
        {
            var claims = new List<LandscapeClaim> { new() { Layer = TextureLayer, Material = Material } };
            if (HeightLayer != null)
            {
                claims.Add(new LandscapeClaim { Layer = HeightLayer, Material = Material });
            }

            return [new LandscapeClaimGroup { Key = Key, Label = Key, Priority = ClaimPriority, Claims = claims }];
        }

        public void Rasterize(in LandscapeRasterContext context)
        {
            if (context.Resolution.IsDropped(Key) ||
                context.Channel(MaskChannel) is not { } channel ||
                context.Buffer(MaskChannel) is not { } buffer)
            {
                return;
            }

            int resolution = channel.Resolution;
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    Vector3 world = context.TexelCentre(resolution, x, y);
                    float distance = new Vector2(world.X - Centre.X, world.Z - Centre.Z).Length();
                    if (distance <= Radius)
                    {
                        buffer[(y * resolution) + x] = 1.0f;
                    }
                }
            }
        }
    }

    private sealed class Fixture
    {
        public required LandscapeSettings Settings { get; init; }
        public required LandscapeCatalog Catalog { get; init; }
        public required LandscapeFunctions Functions { get; init; }
        public required LandscapeLayer Base { get; init; }
        public required LandscapeLayer Texture { get; init; }
        public required LandscapeLayer Height { get; init; }
        public required LandscapeMaterial Material { get; init; }

        public LandscapeBuilder Builder() => new(Settings, Catalog, Functions);
    }

    private static Fixture Build(int textureLimit = 4, float heightAmount = 10.0f)
    {
        var functions = new LandscapeFunctions();
        functions.Discover(typeof(ChannelMaskAlpha).Assembly);

        var alphaValues = new LandscapeParameterValues();
        alphaValues.Set(ChannelMaskAlpha.Mask, MaskChannel);
        alphaValues.Set(ChannelMaskAlpha.Threshold, 0.5f);
        alphaValues.Set(ChannelMaskAlpha.Softness, 0.0f);

        var heightValues = new LandscapeParameterValues();
        heightValues.Set(ChannelHeightOffset.Mask, MaskChannel);
        heightValues.Set(ChannelHeightOffset.Amount, heightAmount);

        var material = new LandscapeMaterial
        {
            Name = "dirt",
            RecordId = 1,
            TexturePath = "res://dirt.png",
            AlphaFunction = "builtin.alpha.channel_mask",
            AlphaParameters = alphaValues.Serialize(),
            HeightFunction = "builtin.height.channel_offset",
            HeightParameters = heightValues.Serialize(),
        };

        var baseLayer = new LandscapeLayer { Name = "ground", RecordId = 1, DrawOrder = 0, IsBase = true };
        var texture = new LandscapeLayer { Name = "detail", RecordId = 2, DrawOrder = 1 };
        var height = new LandscapeLayer
        {
            Name = "raise", RecordId = 3, DrawOrder = 2, Kind = LandscapeLayerKind.Height,
        };
        var channel = new LandscapeChannel { Name = MaskChannel, RecordId = 1, Resolution = 32 };

        var settings = new LandscapeSettings
        {
            ChunkWorldSize = 64.0f,
            ChunkHeightResolution = 17,
            ChunkAlphaResolution = 32,
            TextureLimit = textureLimit,
            FallbackMaterialId = 1,
        };

        return new Fixture
        {
            Settings = settings,
            Catalog = new LandscapeCatalog([channel], [baseLayer, texture, height], [material], functions),
            Functions = functions,
            Base = baseLayer,
            Texture = texture,
            Height = height,
            Material = material,
        };
    }

    private static Disc DiscAt(Fixture fixture, string key, Vector3 centre, float radius, bool deformsHeight = true) =>
        new()
        {
            Key = key,
            Centre = centre,
            Radius = radius,
            TextureLayer = fixture.Texture,
            HeightLayer = deformsHeight ? fixture.Height : null,
            Material = fixture.Material,
        };

    [EditorTest(Category = "LandscapeBuilder", Thread = TestThread.Background)]
    public static void A_deformer_raises_height_and_paints_alpha()
    {
        Fixture fixture = Build();
        Disc disc = DiscAt(fixture, "disc", new Vector3(32.0f, 0.0f, 32.0f), 20.0f);

        LandscapeChunkOutput output = fixture.Builder().BuildOne(new ChunkCoord(0, 0), [disc]);

        Assert.AreEqual(2, output.Layers.Count, "base plus the claimed detail layer");
        Assert.IsTrue(output.Heights.Any(height => height > 9.0f), "the disc centre should be raised");
        Assert.IsTrue(output.Heights.Any(height => height == 0.0f), "the chunk corners are outside the disc");

        byte[] alpha = output.Layers[1].Alpha!;
        Assert.IsTrue(alpha.Any(a => a > 200), "covered texels");
        Assert.IsTrue(alpha.Any(a => a == 0), "uncovered texels");
    }

    [EditorTest(Category = "LandscapeBuilder", Thread = TestThread.Background)]
    public static void An_empty_chunk_falls_back_to_the_base_and_stays_flat()
    {
        Fixture fixture = Build();

        LandscapeChunkOutput output = fixture.Builder().BuildOne(new ChunkCoord(9, 9), []);

        Assert.AreEqual(1, output.Layers.Count);
        Assert.IsTrue(output.Layers[0].IsBase);
        Assert.IsTrue(output.Heights.All(height => height == 0.0f));
    }

    [EditorTest(Category = "LandscapeBuilder", Thread = TestThread.Background)]
    public static void Building_the_same_block_twice_gives_identical_bytes()
    {
        // Guards against any dependence on iteration order, dictionary ordering, or leftover pool
        // state. Once builds are parallel this is the test that catches a shared-state mistake.
        Fixture fixture = Build();
        List<ChunkCoord> block = [new(0, 0), new(1, 0), new(0, 1), new(1, 1)];
        var deformers = new List<ILandscapeDeformer>
        {
            DiscAt(fixture, "a", new Vector3(60.0f, 0.0f, 60.0f), 30.0f),
            DiscAt(fixture, "b", new Vector3(10.0f, 0.0f, 90.0f), 25.0f),
        };

        string first = Describe(fixture.Builder().Build(block, deformers));

        // A fresh builder, and the same builder reused, must both reproduce it.
        LandscapeBuilder reused = fixture.Builder();
        Assert.AreEqual(first, Describe(reused.Build(block, deformers)));
        Assert.AreEqual(first, Describe(reused.Build(block, deformers)), "the channel pool must reset between builds");
        Assert.AreEqual(first, Describe(fixture.Builder().Build(block, deformers.AsEnumerable().Reverse().ToList())));
    }

    [EditorTest(Category = "LandscapeBuilder", Thread = TestThread.Background)]
    public static void Neighbouring_chunks_agree_on_their_shared_height_edge()
    {
        // The seam test. Chunk edge rows are shared, so if a function read chunk-local state — or
        // sampled a channel relative to its own chunk rather than in world space — the two sides
        // would disagree and the terrain would crack along every chunk border.
        Fixture fixture = Build();

        // A disc straddling the border between (0,0) and (1,0), so the edge is not trivially zero.
        Disc disc = DiscAt(fixture, "straddle", new Vector3(64.0f, 0.0f, 32.0f), 28.0f);
        LandscapeBuildResult result = fixture.Builder().Build([new ChunkCoord(0, 0), new ChunkCoord(1, 0)], [disc]);

        LandscapeChunkOutput left = result.Chunks[new ChunkCoord(0, 0)];
        LandscapeChunkOutput right = result.Chunks[new ChunkCoord(1, 0)];
        int resolution = left.HeightResolution;

        bool sawDeformation = false;
        for (int y = 0; y < resolution; y++)
        {
            float leftEdge = left.HeightAt(resolution - 1, y);
            float rightEdge = right.HeightAt(0, y);

            Assert.AreApproximatelyEqual(leftEdge, rightEdge, 1e-4,
                $"height disagrees at shared edge row {y}");
            sawDeformation |= leftEdge > 0.1f;
        }

        Assert.IsTrue(sawDeformation, "the disc must actually reach the shared edge for this to prove anything");
    }

    [EditorTest(Category = "LandscapeBuilder", Thread = TestThread.Background)]
    public static void A_deformer_reaches_every_chunk_its_bounds_touch()
    {
        Fixture fixture = Build();
        Disc disc = DiscAt(fixture, "wide", new Vector3(64.0f, 0.0f, 64.0f), 40.0f);

        LandscapeBuildResult result = fixture.Builder().Build(
            [new ChunkCoord(0, 0), new ChunkCoord(1, 0), new ChunkCoord(0, 1), new ChunkCoord(1, 1)],
            [disc]);

        foreach (LandscapeChunkOutput output in result.Chunks.Values)
        {
            Assert.IsTrue(output.Heights.Any(height => height > 0.1f),
                "a disc centred on the shared corner should reach all four chunks");
        }
    }

    [EditorTest(Category = "LandscapeBuilder", Thread = TestThread.Background)]
    public static void A_dropped_claim_is_reported_against_its_chunk()
    {
        // Two discs wanting one layer with different materials, in a chunk they both reach.
        Fixture fixture = Build();
        var rival = new LandscapeMaterial
        {
            Name = "stone",
            RecordId = 2,
            TexturePath = "res://stone.png",
            AlphaFunction = "builtin.alpha.channel_mask",
            AlphaParameters = fixture.Material.AlphaParameters,
        };

        Disc first = DiscAt(fixture, "a", new Vector3(32.0f, 0.0f, 32.0f), 20.0f);
        var second = new Disc
        {
            Key = "b",
            Centre = new Vector3(32.0f, 0.0f, 32.0f),
            Radius = 20.0f,
            TextureLayer = fixture.Texture,
            Material = rival,
            ClaimPriority = 10,
        };

        LandscapeBuildResult result = fixture.Builder().Build([new ChunkCoord(0, 0)], [first, second]);

        Assert.IsTrue(result.Problems.Any(entry =>
            entry.Coord == new ChunkCoord(0, 0) && entry.Problem.Kind == LandscapeProblemKind.LayerConflict));
        Assert.AreEqual(rival, result.Chunks[new ChunkCoord(0, 0)].Layers[1].Material, "the higher priority wins");
    }

    // A stable, comparable rendering of an entire build.
    private static string Describe(LandscapeBuildResult result)
    {
        var parts = new List<string>();
        foreach (ChunkCoord coord in result.Chunks.Keys.OrderBy(c => c.Y).ThenBy(c => c.X))
        {
            LandscapeChunkOutput output = result.Chunks[coord];
            parts.Add($"{coord}:h[{string.Join(",", output.Heights.Select(h => h.ToString("F6")))}]");
            foreach (LandscapeChunkLayer layer in output.Layers)
            {
                parts.Add($"{layer.Material?.Name}:{(layer.Alpha == null ? "base" : Convert.ToBase64String(layer.Alpha))}");
            }
        }

        return string.Join("|", parts);
    }
}
