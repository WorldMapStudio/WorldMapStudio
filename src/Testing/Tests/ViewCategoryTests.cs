using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Covers <see cref="ViewCategorySystem"/>'s union rule and its <see cref="ViewCategorySystem.Visible"/>
/// cache, and <see cref="ModelFormatViewCategory"/>'s membership rule over both halves of the format
/// axis: an imported <see cref="ModelRendererComponent"/> placement and an authored
/// <see cref="ProceduralComponent"/> output.
/// </summary>
public static class ViewCategoryTests
{
    private sealed class FakeCategory(string id, bool includeAll) : IViewCategory
    {
        public string Id => id;
        public string DisplayName => id;
        public string Group => "Test";
        public bool Includes(SceneEntity entity) => includeAll;
    }

    // A slot with no SupportedFormats restriction, so ProceduralSystem.ResolveFormatId falls back to
    // MeshModelFormat.FormatId without needing anything bound in ProceduralModel.Formats.
    private sealed class FakeMeshFunction : IProceduralFunction
    {
        public string Id => "test.fake_mesh";
        public string DisplayName => "Fake Mesh";
        public string Description => "";
        public int Version => 1;
        public IReadOnlyList<MeshParameter> Parameters { get; } = [];
        public IReadOnlyList<ProceduralOutputSlot> Outputs { get; } = [new ProceduralOutputSlot("mesh", "Mesh", [], [])];
        public void Build(in ProceduralBuildContext context, ProceduralOutputBuilder output) { }
    }

    [EditorTest(Category = "View", Thread = TestThread.Background)]
    public static void Model_format_category_includes_a_matching_model_renderer()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_view_category_renderer_test__" });
        IModelFormat format = context.ModelFormats.Find(ObjModelFormat.FormatId)!;
        var category = new ModelFormatViewCategory(format, context.Assets, context.Procedural);

        var entity = new MapSceneEntity();
        entity.AddComponent(new ModelRendererComponent(context.Assets, context.MeshMaterials) { ModelPath = "thing.obj" });

        Assert.IsTrue(category.Includes(entity));
    }

    [EditorTest(Category = "View", Thread = TestThread.Background)]
    public static void Model_format_category_includes_a_bound_procedural_output()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_view_category_procedural_test__" });
        context.Procedural.DiscoverFrom([new FakeMeshFunction()]);

        var model = new ProceduralModel { RecordId = 1, FunctionId = "test.fake_mesh" };
        context.Catalog.Add(model);

        var entity = new MapSceneEntity();
        entity.AddComponent(new ProceduralComponent(context.Procedural) { ModelId = model.RecordId });

        IModelFormat mesh = context.ModelFormats.Find(MeshModelFormat.FormatId)!;
        var category = new ModelFormatViewCategory(mesh, context.Assets, context.Procedural);

        Assert.IsTrue(category.Includes(entity), "an output slot with no bound format defaults to the plain mesh format");
    }

    [EditorTest(Category = "View", Thread = TestThread.Background)]
    public static void Model_format_category_excludes_an_entity_with_neither_component()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_view_category_neither_test__" });
        IModelFormat format = context.ModelFormats.Find(ObjModelFormat.FormatId)!;
        var category = new ModelFormatViewCategory(format, context.Assets, context.Procedural);

        Assert.IsFalse(category.Includes(new MapSceneEntity()));
    }

    [EditorTest(Category = "View", Thread = TestThread.Background)]
    public static void Hidden_is_true_when_any_including_category_is_hidden()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_view_category_union_test__" });
        var entity = new MapSceneEntity();
        context.Scene.Add(entity);

        var a = new FakeCategory("test.a", includeAll: false);
        var b = new FakeCategory("test.b", includeAll: true);
        context.ViewCategories.DiscoverFrom([a, b]);

        context.ViewCategories.SetHidden("test.a", true);
        Assert.IsFalse(context.ViewCategories.IsHidden(entity), "hiding a category the entity is not in leaves it visible");

        context.ViewCategories.SetHidden("test.b", true);
        Assert.IsTrue(context.ViewCategories.IsHidden(entity), "hiding any including category hides it");
    }

    [EditorTest(Category = "View", Thread = TestThread.Background)]
    public static void Visible_invalidates_on_scene_and_filter_version()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_view_category_visible_test__" });
        var everything = new FakeCategory("test.everything", includeAll: true);
        context.ViewCategories.DiscoverFrom([everything]);

        var first = new MapSceneEntity();
        context.Scene.Add(first);
        Assert.AreEqual(1, context.ViewCategories.Visible.Count, "a newly loaded entity is visible by default");

        context.ViewCategories.SetHidden("test.everything", true);
        Assert.AreEqual(0, context.ViewCategories.Visible.Count, "the cache must notice the filter version moved");

        context.ViewCategories.SetHidden("test.everything", false);
        var second = new MapSceneEntity();
        context.Scene.Add(second);
        Assert.AreEqual(2, context.ViewCategories.Visible.Count, "the cache must notice the scene version moved");
    }
}
