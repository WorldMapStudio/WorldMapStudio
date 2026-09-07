using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Covers the offline build preparation stage: the two deformers that are inert when scanned from
/// storage (image, procedural) must produce the same terrain a streamed-in placement would, and a
/// map must resolve against its own catalog. The first two failures are silent — they produce flat,
/// unpainted terrain rather than an error — which is exactly why they are worth a test.
/// </summary>
public static class TerrainExportPreparationTests
{
    private const string PaintChannel = "paint";
    private const string CentreChannel = "road_centre";
    private const string ShoulderChannel = "road_shoulder";

    private static EditorContext NewContext(string name) =>
        new(new Node3D(), new Project { Name = name });

    [EditorTest(Category = "LandscapeBuild", Thread = TestThread.Main)]
    public static void Prepared_image_chunks_rasterize_without_residency()
    {
        var functions = new LandscapeFunctions();
        functions.Discover(typeof(ChannelMaskAlpha).Assembly);

        var alphaValues = new LandscapeParameterValues();
        alphaValues.Set(ChannelMaskAlpha.Mask, PaintChannel);
        alphaValues.Set(ChannelMaskAlpha.Threshold, 0.5f);
        alphaValues.Set(ChannelMaskAlpha.Softness, 0.0f);

        var painted = new LandscapeMaterial
        {
            Name = "painted",
            RecordId = 1,
            TexturePath = "res://painted.png",
            AlphaFunction = "builtin.alpha.channel_mask",
            AlphaParameters = alphaValues.Serialize(),
        };
        var ground = new LandscapeMaterial { Name = "ground", RecordId = 2, TexturePath = "res://ground.png" };
        var baseLayer = new LandscapeLayer { Name = "base", RecordId = 1, DrawOrder = 0, IsBase = true };
        var paintLayer = new LandscapeLayer { Name = "paint", RecordId = 2, DrawOrder = 1 };
        var channel = new LandscapeChannel { Name = PaintChannel, RecordId = 1, Resolution = 32 };

        var settings = new LandscapeSettings
        {
            ChunkWorldSize = 64.0f,
            ChunkHeightResolution = 9,
            ChunkAlphaResolution = 32,
            TextureLimit = 4,
            FallbackMaterialId = 2,
        };
        var catalog = new LandscapeCatalog([channel], [baseLayer, paintLayer], [painted, ground], functions);

        EditorContext context = NewContext("__wms_prepared_image_test__");
        PaintImage image = new() { RecordId = 1, Name = "Painted" };
        context.Catalog.Add(image);
        image.ConfigureNew(64, 64, chunkSize: 16);

        // Full-strength over the whole canvas, so every chunk holds solid pixels.
        image.Paint(0.5f, 0.5f, 10.0f, 10.0f, 1.0f, erase: false);
        Assert.Greater(image.ChunkCount, 0);

        // A detached table built straight from the painted chunks — what the preparation stage would
        // hand a scanned placement — then evict everything so nothing is resident.
        List<ImageChunkCoord> coords = image.ChunkCoords.ToList();
        var table = new ImageChunkTable(coords.ToDictionary(coord => coord, coord => new ImageChunk(image.CopyChunkBytes(coord)!)));

        image.MarkChunksClean(coords);
        foreach (ImageChunkCoord coord in coords)
        {
            image.EvictChunk(coord);
        }

        Assert.AreEqual(0, image.ChunkCount, "nothing resident");

        var entity = new SceneEntity();
        var painter = new ImageComponent(context.Images)
        {
            ImageId = image.RecordId,
            Channel = PaintChannel,
            WorldSizeX = 64.0f,
            WorldSizeZ = 64.0f,
        };
        var bind = new LandscapeMaterialBindComponent();
        bind.ReplaceBindings([new LandscapeMaterialBinding(paintLayer.RecordId, painted.RecordId)]);
        entity.AddComponent(painter);
        entity.AddComponent(bind);
        entity.Transform = new Transform3D(Basis.Identity, new Vector3(32.0f, 0.0f, 32.0f));

        var resources = new LandscapeBuildResources(new Dictionary<PaintImage, ImageChunkTable> { [image] = table });
        ((IPreparableLandscapeDeformer)painter).Prepare(resources);

        LandscapeChunkOutput output = new LandscapeBuilder(settings, catalog, functions)
            .BuildOne(new ChunkCoord(0, 0), entity.Components.OfType<ILandscapeDeformer>().ToList());

        LandscapeChunkLayer? slot = output.Layers.FirstOrDefault(layer => layer.Material == painted);
        Assert.IsNotNull(slot, "the painted layer should have claimed a slot");
        Assert.IsTrue(slot!.Alpha!.Any(alpha => alpha > 200),
            "the prepared chunk table must paint even though nothing is resident");
    }

    [EditorTest(Category = "LandscapeBuild", Thread = TestThread.Main)]
    public static void An_unrepresented_procedural_placement_still_paints()
    {
        var functions = new LandscapeFunctions();
        functions.Discover(typeof(ChannelMaskAlpha).Assembly);

        var centreValues = new LandscapeParameterValues();
        centreValues.Set(ChannelMaskAlpha.Mask, CentreChannel);
        centreValues.Set(ChannelMaskAlpha.Threshold, 0.5f);
        centreValues.Set(ChannelMaskAlpha.Softness, 0.0f);

        var centreMaterial = new LandscapeMaterial
        {
            Name = "road_centre_mat",
            RecordId = 1,
            TexturePath = "res://road_centre.png",
            AlphaFunction = "builtin.alpha.channel_mask",
            AlphaParameters = centreValues.Serialize(),
        };
        var ground = new LandscapeMaterial { Name = "ground", RecordId = 2, TexturePath = "res://ground.png" };
        var baseLayer = new LandscapeLayer { Name = "ground", RecordId = 1, DrawOrder = 0, IsBase = true };
        var centreLayer = new LandscapeLayer { Name = "road_centre", RecordId = 2, DrawOrder = 1 };
        var channelCentre = new LandscapeChannel { Name = CentreChannel, RecordId = 1, Resolution = 32 };
        var channelShoulder = new LandscapeChannel { Name = ShoulderChannel, RecordId = 2, Resolution = 32 };

        var settings = new LandscapeSettings
        {
            ChunkWorldSize = 64.0f,
            ChunkHeightResolution = 17,
            ChunkAlphaResolution = 32,
            TextureLimit = 4,
            FallbackMaterialId = 2,
        };
        var catalog = new LandscapeCatalog(
            [channelCentre, channelShoulder], [baseLayer, centreLayer], [centreMaterial, ground], functions);

        EditorContext context = NewContext("__wms_unrepresented_procedural_test__");

        var model = new ProceduralModel { RecordId = 1, FunctionId = "builtin.procedural.road" };
        var meshValues = new MeshParameterValues();
        meshValues.Set(RoadNetworkFunction.CentreWidth, 8.0f);
        meshValues.Set(RoadNetworkFunction.ShoulderWidth, 6.0f);
        meshValues.Set(RoadNetworkFunction.Falloff, 0.3f);
        meshValues.Set(RoadNetworkFunction.CentreChannel, CentreChannel);
        meshValues.Set(RoadNetworkFunction.ShoulderChannel, ShoulderChannel);
        model.Parameters = meshValues.Serialize();

        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(-40.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(40.0f, 0.0f, 0.0f));
        network.AddEdge(a, b);
        model.ReplaceNetwork(network);
        context.Catalog.Add(model);

        var road = new ProceduralComponent(context.Procedural) { ModelId = model.RecordId };
        var bind = new LandscapeMaterialBindComponent();
        bind.ReplaceBindings([new LandscapeMaterialBinding(centreLayer.RecordId, centreMaterial.RecordId)]);

        var entity = new SceneEntity();
        entity.AddComponent(road);
        entity.AddComponent(bind);
        entity.Transform = new Transform3D(Basis.Identity, new Vector3(32.0f, 0.0f, 32.0f));
        context.Scene.Add(entity);

        // Never represented: no CreateRepresentation call, so BuildNode has never run.
        Assert.IsTrue(road.InfluenceBounds.Size == Vector3.Zero,
            "an unrepresented placement contributes nothing until its paint is published");

        ((IPreparableLandscapeDeformer)road).Prepare(new LandscapeBuildResources(new Dictionary<PaintImage, ImageChunkTable>()));

        Assert.IsTrue(road.InfluenceBounds.Size != Vector3.Zero, "publishing the paint gives it a footprint");

        LandscapeChunkOutput output = new LandscapeBuilder(settings, catalog, functions)
            .BuildOne(new ChunkCoord(0, 0), entity.Components.OfType<ILandscapeDeformer>().ToList());

        LandscapeChunkLayer? slot = output.Layers.FirstOrDefault(layer => layer.Material == centreMaterial);
        Assert.IsNotNull(slot, "the road centre layer should have claimed a slot");
        Assert.IsTrue(slot!.Alpha!.Any(alpha => alpha > 200), "the road must paint after its contribution is published");
    }

    [EditorTest(Category = "Landscape", Thread = TestThread.Main)]
    public static void A_map_resolves_its_own_catalog()
    {
        EditorContext context = NewContext("__wms_per_map_catalog_test__");

        var mapOne = new MapId(1);
        var mapTwo = new MapId(2);
        var layerOne = new LandscapeLayer { Name = "one", RecordId = 1, DrawOrder = 0, Map = mapOne };
        var layerTwo = new LandscapeLayer { Name = "two", RecordId = 2, DrawOrder = 0, Map = mapTwo };
        context.Catalog.Add(layerOne);
        context.Catalog.Add(layerTwo);

        LandscapeCatalog catalogOne = context.Landscape.CatalogFor(mapOne);
        LandscapeCatalog catalogTwo = context.Landscape.CatalogFor(mapTwo);

        Assert.IsTrue(catalogOne.Layers.Contains(layerOne));
        Assert.IsFalse(catalogOne.Layers.Contains(layerTwo), "map one must not see map two's layer");
        Assert.IsTrue(catalogTwo.Layers.Contains(layerTwo));
        Assert.IsFalse(catalogTwo.Layers.Contains(layerOne), "map two must not see map one's layer");
    }
}
