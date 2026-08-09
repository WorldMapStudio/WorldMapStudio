using System.Linq;
using Godot;

namespace WorldMapStudio;

public static class DrawingTargetTests
{
    private const string MaskChannel = "paint";

    [EditorTest(Category = "DrawingTarget", Thread = TestThread.Background)]
    public static void Painting_changes_the_bitmap_and_content_version()
    {
        var target = new DrawingTargetEntity();
        target.Resize(32, 32);
        int beforeVersion = target.ContentVersion;

        bool changed = target.Paint(Vector3.Zero, 6.0f, 1.0f, erase: false);

        Assert.IsTrue(changed);
        Assert.IsTrue(target.Pixels.ToArray().Any(pixel => pixel > 0));
        Assert.AreNotEqual(beforeVersion, target.ContentVersion);
    }

    [EditorTest(Category = "DrawingTarget", Thread = TestThread.Background)]
    public static void Rasterizing_a_drawing_target_feeds_the_landscape_channel()
    {
        var functions = new LandscapeFunctions();
        functions.Discover(typeof(ChannelMaskAlpha).Assembly);

        var alphaValues = new LandscapeParameterValues();
        alphaValues.Set(ChannelMaskAlpha.Mask, MaskChannel);
        alphaValues.Set(ChannelMaskAlpha.Threshold, 0.1f);
        alphaValues.Set(ChannelMaskAlpha.Softness, 0.0f);

        var material = new LandscapeMaterial
        {
            Name = "painted",
            RecordId = 1,
            TexturePath = "res://painted.png",
            AlphaFunction = "builtin.alpha.channel_mask",
            AlphaParameters = alphaValues.Serialize(),
        };

        var channel = new LandscapeChannel { Name = MaskChannel, RecordId = 1, Resolution = 32 };
        var baseLayer = new LandscapeLayer { Name = "base", RecordId = 1, DrawOrder = 0, IsBase = true };
        var paintLayer = new LandscapeLayer { Name = "paint", RecordId = 2, DrawOrder = 1 };
        var settings = new LandscapeSettings
        {
            ChunkWorldSize = 64.0f,
            ChunkHeightResolution = 9,
            ChunkAlphaResolution = 32,
            TextureLimit = 4,
            FallbackMaterialId = 1,
        };

        var catalog = new LandscapeCatalog([channel], [baseLayer, paintLayer], [material], functions);
        var target = new DrawingTargetEntity
        {
            Channel = MaskChannel,
            LayerId = paintLayer.RecordId,
            MaterialId = material.RecordId,
            WorldSizeX = 64.0f,
            WorldSizeZ = 64.0f,
        };
        target.Paint(Vector3.Zero, 12.0f, 1.0f, erase: false);

        LandscapeChunkOutput output = new LandscapeBuilder(settings, catalog, functions)
            .BuildOne(new ChunkCoord(0, 0), [target]);

        Assert.AreEqual(2, output.Layers.Count);
        Assert.IsTrue(output.Layers[1].Alpha!.Any(alpha => alpha > 200), "painted pixels should become alpha");
        Assert.IsTrue(output.Layers[1].Alpha!.Any(alpha => alpha == 0), "unpainted pixels should stay transparent");
    }
}
