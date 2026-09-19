using Godot;

namespace WorldMapStudio;

/// <summary>Covers <see cref="TagSystem"/>: tag names, tagging and its undo, and tag deletion.</summary>
public static class TagSystemTests
{
    private sealed class ExternalEntity : SceneEntity
    {
    }

    private static EditorContext NewContext() =>
        new(new Node3D(), new Project { Name = "__wms_tag_system_test__" });

    [EditorTest(Category = "Tags", Thread = TestThread.Main)]
    public static void Tag_names_must_be_unique_and_searchable()
    {
        EditorContext context = NewContext();
        context.Tags.Create("town");

        Assert.IsNotNull(context.Tags.NameProblem("Town"), "names are unique whatever their case");
        Assert.IsNotNull(context.Tags.NameProblem("two words"));
        Assert.IsNotNull(context.Tags.NameProblem("a:b"));
        Assert.IsNotNull(context.Tags.NameProblem("  "));
        Assert.IsNull(context.Tags.NameProblem("wip"));
    }

    [EditorTest(Category = "Tags", Thread = TestThread.Main)]
    public static void Tagging_a_selection_is_one_undo_step_and_leaves_existing_tags_alone()
    {
        EditorContext context = NewContext();
        EntityTagDefinition existing = context.Tags.Create("old");
        EntityTagDefinition added = context.Tags.Create("new");
        var a = new MapSceneEntity();
        var b = new MapSceneEntity();
        context.Scene.Add(a);
        context.Scene.Add(b);
        context.Tags.Add([a], existing.RecordId!.Value);

        int changed = context.Tags.Add([a, b], added.RecordId!.Value);

        Assert.AreEqual(2, changed);
        Assert.IsTrue(a.Tags.Contains(existing.RecordId!.Value) && a.Tags.Contains(added.RecordId!.Value));
        Assert.IsTrue(b.Tags.Contains(added.RecordId!.Value));

        context.EditSessions.Undo();
        Assert.IsFalse(a.Tags.Contains(added.RecordId!.Value));
        Assert.IsTrue(a.Tags.Contains(existing.RecordId!.Value), "the earlier tag survives the undo");
        Assert.IsTrue(b.Tags.IsEmpty);
    }

    [EditorTest(Category = "Tags", Thread = TestThread.Main)]
    public static void An_entity_with_nowhere_to_keep_tags_is_not_tagged()
    {
        EditorContext context = NewContext();
        EntityTagDefinition tag = context.Tags.Create("x");
        var external = new ExternalEntity();
        context.Scene.Add(external);

        Assert.IsFalse(context.Tags.CanTag(external));
        Assert.AreEqual(0, context.Tags.Add([external], tag.RecordId!.Value));
        Assert.IsTrue(external.Tags.IsEmpty);
    }

    [EditorTest(Category = "Tags", Thread = TestThread.Main)]
    public static void Deleting_a_tag_strips_it_from_loaded_entities_and_undoes_together()
    {
        EditorContext context = NewContext();
        EntityTagDefinition tag = context.Tags.Create("gone");
        var entity = new MapSceneEntity();
        context.Scene.Add(entity);
        context.Tags.Add([entity], tag.RecordId!.Value);

        context.Tags.Delete(tag);
        Assert.IsTrue(entity.Tags.IsEmpty);
        Assert.IsNull(context.Tags.Find(tag.RecordId!.Value));

        context.EditSessions.Undo();
        Assert.IsTrue(entity.Tags.Contains(tag.RecordId!.Value));
        Assert.IsNotNull(context.Tags.Find(tag.RecordId!.Value));
    }

    [EditorTest(Category = "Tags", Thread = TestThread.Main)]
    public static void Creating_a_tag_on_a_selection_is_one_undo_step()
    {
        EditorContext context = NewContext();
        var entity = new MapSceneEntity();
        context.Scene.Add(entity);

        EntityTagDefinition tag = context.Tags.Create("fresh", tagWith: [entity]);
        Assert.IsTrue(entity.Tags.Contains(tag.RecordId!.Value));

        context.EditSessions.Undo();
        Assert.IsTrue(entity.Tags.IsEmpty);
        Assert.IsNull(context.Tags.Find(tag.RecordId!.Value));
    }
}
