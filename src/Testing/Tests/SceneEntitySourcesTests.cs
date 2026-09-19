using Godot;

namespace WorldMapStudio;

/// <summary>Covers <see cref="SceneEntitySources"/>' type-to-factory lookup.</summary>
public static class SceneEntitySourcesTests
{
    private sealed class UnstoredEntity : SceneEntity
    {
    }

    [EditorTest(Category = "Bridge", Thread = TestThread.Main)]
    public static void A_native_entity_resolves_to_its_factory_and_is_not_bridged()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_scene_sources_test__" });
        SceneEntitySources sources = context.Database.SceneSources;
        var entity = new MapSceneEntity { RecordId = 12 };

        Assert.IsTrue(sources.FactoryFor(entity) is MapSceneEntityFactory);
        Assert.IsTrue(sources.StorageOf(entity) is EditorStorage);
        Assert.IsNull(sources.SourceOf(entity), "native entities are rows of the entity table, not bridged");
    }

    [EditorTest(Category = "Bridge", Thread = TestThread.Main)]
    public static void An_entity_no_factory_persists_resolves_to_nothing()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_scene_sources_test__" });
        var entity = new UnstoredEntity();

        Assert.IsNull(context.Database.SceneSources.FactoryFor(entity));
        Assert.IsNull(context.Database.SceneSources.StorageOf(entity));
        Assert.IsNull(context.Database.SceneSources.SourceOf(entity));
    }
}
