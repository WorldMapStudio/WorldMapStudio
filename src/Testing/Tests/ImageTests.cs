using System.Linq;
using Godot;

namespace WorldMapStudio;

public static class ImageTests
{
    private const string MaskChannel = "paint";

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Painting_changes_the_bitmap_and_content_version()
    {
        EditorContext context = NewContext("__wms_image_paint_test__");
        PaintImage image = NewImage(context, id: 1);
        image.Resize(32, 32);

        var entity = new SceneEntity();
        var target = new ImageComponent(context.Images) { ImageId = image.RecordId };
        entity.AddComponent(target);
        int beforeVersion = target.ContentVersion;

        bool changed = target.Paint(Vector3.Zero, 6.0f, 1.0f, erase: false);

        Assert.IsTrue(changed);
        Assert.IsTrue(image.Pixels.ToArray().Any(pixel => pixel > 0));
        Assert.AreNotEqual(beforeVersion, target.ContentVersion);
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Images_ignore_authored_height_and_tilt()
    {
        EditorContext context = NewContext("__wms_image_transform_test__");
        var target = new SceneEntity();
        target.AddComponent(new ImageComponent(context.Images));
        target.Transform = new Transform3D(
            Basis.FromEuler(new Vector3(0.35f, 0.7f, -0.2f)),
            new Vector3(12.0f, 99.0f, 24.0f));

        Assert.AreApproximatelyEqual(0.0, target.Transform.Origin.Y, 1e-5);
        Assert.AreApproximatelyEqual(0.0, target.Transform.Basis.X.Y, 1e-5);
        Assert.AreApproximatelyEqual(1.0, target.Transform.Basis.Y.Y, 1e-5);
        Assert.AreApproximatelyEqual(0.0, target.Transform.Basis.Z.Y, 1e-5);
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Rasterizing_an_image_feeds_the_landscape_channel()
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
        EditorContext context = NewContext("__wms_image_rasterize_test__");
        PaintImage image = NewImage(context, id: 1);
        var entity = new SceneEntity();
        var target = new ImageComponent(context.Images)
        {
            ImageId = image.RecordId,
            Channel = MaskChannel,
            WorldSizeX = 64.0f,
            WorldSizeZ = 64.0f,
        };
        var bind = new LandscapeMaterialBindComponent();
        bind.ReplaceBindings([new LandscapeMaterialBinding(paintLayer.RecordId, material.RecordId)]);
        entity.AddComponent(target);
        entity.AddComponent(bind);
        target.Paint(Vector3.Zero, 12.0f, 1.0f, erase: false);

        LandscapeChunkOutput output = new LandscapeBuilder(settings, catalog, functions)
            .BuildOne(new ChunkCoord(0, 0), entity.Components.OfType<ILandscapeDeformer>().ToList());

        Assert.AreEqual(2, output.Layers.Count);
        Assert.IsTrue(output.Layers[1].Alpha!.Any(alpha => alpha > 200), "painted pixels should become alpha");
        Assert.IsTrue(output.Layers[1].Alpha!.Any(alpha => alpha == 0), "unpainted pixels should stay transparent");
    }

    private static EditorContext NewContext(string name) =>
        new(new Node3D(), new Project { Name = name });

    private static PaintImage NewImage(EditorContext context, int id)
    {
        var image = new PaintImage { RecordId = id, Name = $"Image {id}" };
        context.Catalog.Add(image);
        return image;
    }
}
