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
    public static void Images_keep_authored_height_but_only_yaw_rotation()
    {
        EditorContext context = NewContext("__wms_image_transform_test__");
        var target = new SceneEntity();
        target.AddComponent(new ImageComponent(context.Images));
        target.Transform = new Transform3D(
            Basis.FromEuler(new Vector3(0.35f, 0.7f, -0.2f)),
            new Vector3(12.0f, 99.0f, 24.0f));

        // Height has no bearing on the terrain projection (Rasterize/Paint never read local.Y), so
        // it's free — unlike rotation, where only yaw actually changes the projected footprint.
        Assert.AreApproximatelyEqual(99.0, target.Transform.Origin.Y, 1e-5);
        Assert.AreApproximatelyEqual(0.0, target.Transform.Basis.X.Y, 1e-5);
        Assert.AreApproximatelyEqual(1.0, target.Transform.Basis.Y.Y, 1e-5);
        Assert.AreApproximatelyEqual(0.0, target.Transform.Basis.Z.Y, 1e-5);
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Display_layer_setters_bump_revision()
    {
        var layer = new ImageDisplayLayer();
        int revision0 = layer.Revision;

        layer.Name = "Roads";
        Assert.Greater(layer.Revision, revision0);

        int revision1 = layer.Revision;
        layer.DisplayMode = ImageDisplayMode.LandscapeOverlay;
        Assert.Greater(layer.Revision, revision1);

        int revision2 = layer.Revision;
        layer.BaseColor = new Color(0.1f, 0.2f, 0.3f, 0.0f);
        Assert.Greater(layer.Revision, revision2);

        int revision3 = layer.Revision;
        layer.FullColor = new Color(0.1f, 0.2f, 0.3f, 1.0f);
        Assert.Greater(layer.Revision, revision3);
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void None_display_mode_builds_no_node()
    {
        EditorContext context = NewContext("__wms_image_display_none_test__");
        PaintImage image = NewImage(context, id: 1);
        var layer = new ImageDisplayLayer { RecordId = 1, DisplayMode = ImageDisplayMode.None };
        context.Catalog.Add(layer);

        var target = new ImageComponent(context.Images) { ImageId = image.RecordId, DisplayLayerId = layer.RecordId };
        Assert.IsNull(target.BuildNode());
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void LandscapeOverlay_display_mode_builds_a_decal_sized_to_the_footprint()
    {
        EditorContext context = NewContext("__wms_image_display_overlay_test__");
        PaintImage image = NewImage(context, id: 1);
        var layer = new ImageDisplayLayer { RecordId = 1, DisplayMode = ImageDisplayMode.LandscapeOverlay };
        context.Catalog.Add(layer);

        var target = new ImageComponent(context.Images)
        {
            ImageId = image.RecordId,
            DisplayLayerId = layer.RecordId,
            WorldSizeX = 32.0f,
            WorldSizeZ = 48.0f,
        };

        var decal = target.BuildNode() as Decal;
        Assert.IsNotNull(decal);
        Assert.AreApproximatelyEqual(32.0, decal!.Size.X, 1e-5);
        Assert.AreApproximatelyEqual(48.0, decal.Size.Z, 1e-5);
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Object_display_mode_builds_a_paintable_quad_sized_to_the_footprint()
    {
        EditorContext context = NewContext("__wms_image_display_object_test__");
        PaintImage image = NewImage(context, id: 1);
        var layer = new ImageDisplayLayer { RecordId = 1, DisplayMode = ImageDisplayMode.Object };
        context.Catalog.Add(layer);

        var target = new ImageComponent(context.Images)
        {
            ImageId = image.RecordId,
            DisplayLayerId = layer.RecordId,
            WorldSizeX = 20.0f,
            WorldSizeZ = 10.0f,
        };

        var mesh = target.BuildNode() as MeshInstance3D;
        Assert.IsNotNull(mesh);
        var plane = mesh!.Mesh as PlaneMesh;
        Assert.IsNotNull(plane);
        Assert.AreApproximatelyEqual(20.0, plane!.Size.X, 1e-5);
        Assert.AreApproximatelyEqual(10.0, plane.Size.Y, 1e-5);
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void System_update_refreshes_every_placement_when_the_shared_display_layer_changes()
    {
        EditorContext context = NewContext("__wms_image_display_update_test__");
        PaintImage image = NewImage(context, id: 1);
        var layer = new ImageDisplayLayer { RecordId = 1, DisplayMode = ImageDisplayMode.None };
        context.Catalog.Add(layer);

        var entityA = new SceneEntity();
        var entityB = new SceneEntity();
        entityA.AddComponent(new ImageComponent(context.Images) { ImageId = image.RecordId, DisplayLayerId = layer.RecordId });
        entityB.AddComponent(new ImageComponent(context.Images) { ImageId = image.RecordId, DisplayLayerId = layer.RecordId });
        context.Scene.Add(entityA);
        context.Scene.Add(entityB);
        entityA.CreateRepresentation(context.Root);
        entityB.CreateRepresentation(context.Root);

        var componentA = entityA.Component<ImageComponent>()!;
        var componentB = entityB.Component<ImageComponent>()!;
        context.Images.Update();
        Assert.IsFalse(componentA.NeedsRefresh);
        Assert.IsFalse(componentB.NeedsRefresh);

        layer.DisplayMode = ImageDisplayMode.Object;

        Assert.IsTrue(componentA.NeedsRefresh);
        Assert.IsTrue(componentB.NeedsRefresh);

        context.Images.Update();
        Assert.IsFalse(componentA.NeedsRefresh, "the guarded sweep should have refreshed every placement sharing the layer");
        Assert.IsFalse(componentB.NeedsRefresh);
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
