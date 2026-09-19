using System.Linq;

namespace WorldMapStudio;

/// <summary>Covers the split between components a factory builds and components a user attaches.</summary>
public static class IntrinsicComponentTests
{
    [EditorTest(Category = "Components", Thread = TestThread.Background)]
    public static void An_intrinsic_component_is_hidden_from_persistence()
    {
        var entity = new MapSceneEntity();
        entity.AddIntrinsicComponent(new MarkerComponent());

        Assert.IsTrue(entity.Component<MarkerComponent>() is { IsIntrinsic: true });
        Assert.IsNull(entity.Attached<MarkerComponent>(), "persisters read Attached<T>, so a factory-built component never reaches a table");
        Assert.IsFalse(entity.AttachedComponents.Any());
    }

    [EditorTest(Category = "Components", Thread = TestThread.Background)]
    public static void An_attached_component_is_visible_to_persistence()
    {
        var entity = new MapSceneEntity();
        entity.AddComponent(new MarkerComponent());

        Assert.IsNotNull(entity.Attached<MarkerComponent>());
        Assert.AreEqual(1, entity.AttachedComponents.Count());
    }

    [EditorTest(Category = "Components", Thread = TestThread.Background)]
    public static void Clone_leaves_intrinsic_components_out()
    {
        var entity = new MapSceneEntity();
        entity.AddIntrinsicComponent(new MarkerComponent());
        entity.AddComponent(new PrefabTemplateComponent { PrefabId = 3 });

        SceneEntity clone = entity.Clone()!;

        Assert.IsNull(clone.Component<MarkerComponent>());
        Assert.IsNotNull(clone.Component<PrefabTemplateComponent>());
    }
}
