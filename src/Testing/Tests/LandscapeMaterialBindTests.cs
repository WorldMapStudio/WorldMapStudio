using System.Linq;
using Godot;

namespace WorldMapStudio;

public static class LandscapeMaterialBindTests
{
    [EditorTest(Category = "LandscapeMaterialBind", Thread = TestThread.Background)]
    public static void Bind_claims_every_chunk_spanned_by_the_whole_entity()
    {
        var functions = new LandscapeFunctions();
        var material = new LandscapeMaterial
        {
            Name = "grass",
            RecordId = 1,
            TexturePath = "res://grass.png",
        };
        var layer = new LandscapeLayer
        {
            Name = "detail",
            RecordId = 1,
            DrawOrder = 1,
        };
        var settings = new LandscapeSettings
        {
            ChunkWorldSize = 64.0f,
            ChunkHeightResolution = 9,
            ChunkAlphaResolution = 16,
            TextureLimit = 4,
            FallbackMaterialId = 1,
        };
        var catalog = new LandscapeCatalog([], [layer], [material], functions);

        var entity = new MapSceneEntity();
        entity.AddComponent(new StampComponent { Radius = 80.0f });
        var bind = new LandscapeMaterialBindComponent();
        bind.ReplaceBindings([new LandscapeMaterialBinding(layer.RecordId, material.RecordId)]);
        entity.AddComponent(bind);
        entity.Transform = new Transform3D(Basis.Identity, new Vector3(64.0f, 0.0f, 32.0f));

        LandscapeBuildResult result = new LandscapeBuilder(settings, catalog, functions).Build(
            [new ChunkCoord(0, 0), new ChunkCoord(1, 0)],
            [bind]);

        Assert.IsTrue(result.Chunks[new ChunkCoord(0, 0)].Layers.Any(output => output.Material == material));
        Assert.IsTrue(result.Chunks[new ChunkCoord(1, 0)].Layers.Any(output => output.Material == material));
    }
}
