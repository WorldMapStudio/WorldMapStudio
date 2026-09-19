namespace WorldMapStudio;

/// <summary>
/// Covers <see cref="LightingViewCategory"/>'s membership rule: any component implementing
/// <see cref="IEnvironmentSource"/>, so a plugin's own light falls under it without core naming it.
/// </summary>
public static class LightingViewCategoryTests
{
    private sealed class FakeEnvironmentSource : SceneComponent, IEnvironmentSource
    {
        public override string TypeId => "test-environment-source";

        public override string DisplayName => "Test Environment Source";

        public bool IsGlobal => false;

        public float WeightAt(Godot.Vector3 worldPosition) => 1.0f;

        public EnvironmentValues Evaluate(in EnvironmentTime time) => new();

        public override SceneComponent Clone() => new FakeEnvironmentSource();
    }

    private sealed class PlainComponent : SceneComponent
    {
        public override string TypeId => "test-plain";

        public override string DisplayName => "Test Plain";

        public override SceneComponent Clone() => new PlainComponent();
    }

    [EditorTest(Category = "View")]
    public static void Includes_an_entity_whose_component_is_an_environment_source()
    {
        var category = new LightingViewCategory(null!);
        var entity = new MapSceneEntity();
        entity.AddComponent(new FakeEnvironmentSource());

        Assert.IsTrue(category.Includes(entity));
    }

    [EditorTest(Category = "View")]
    public static void Excludes_an_entity_with_no_environment_source_component()
    {
        var category = new LightingViewCategory(null!);
        var entity = new MapSceneEntity();
        entity.AddComponent(new PlainComponent());

        Assert.IsFalse(category.Includes(entity));
    }
}
