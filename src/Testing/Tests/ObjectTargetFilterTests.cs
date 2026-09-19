using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>Covers which entities the Object tool may target once tags constrain it.</summary>
public static class ObjectTargetFilterTests
{
    private sealed class DerivedEntity : SceneEntity, IDerivedEntity
    {
    }

    private static MapSceneEntity Tagged(params int[] tags) => new() { Tags = EntityTagSet.From(tags) };

    [EditorTest(Category = "Tags", Thread = TestThread.Background)]
    public static void With_no_filter_everything_is_targetable()
    {
        var filter = new ObjectTargetFilter();

        Assert.IsTrue(filter.Targets(Tagged()));
        Assert.IsTrue(filter.Targets(Tagged(1, 2)));
        Assert.IsTrue(filter.Targets(new DerivedEntity()));
    }

    [EditorTest(Category = "Tags", Thread = TestThread.Background)]
    public static void Include_admits_only_entities_carrying_one_of_the_tags()
    {
        var filter = new ObjectTargetFilter();
        filter.Set(EntityTagSet.From([1, 2]), EntityTagSet.Empty, includeUntagged: false);

        Assert.IsTrue(filter.Targets(Tagged(2, 9)));
        Assert.IsFalse(filter.Targets(Tagged(9)));
        Assert.IsFalse(filter.Targets(Tagged()), "untagged entities are out unless asked for");
    }

    [EditorTest(Category = "Tags", Thread = TestThread.Background)]
    public static void Include_untagged_admits_entities_with_no_tags()
    {
        var filter = new ObjectTargetFilter();
        filter.Set(EntityTagSet.From([1]), EntityTagSet.Empty, includeUntagged: true);

        Assert.IsTrue(filter.Targets(Tagged()));
        Assert.IsTrue(filter.Targets(Tagged(1)));
        Assert.IsFalse(filter.Targets(Tagged(5)), "an entity tagged with something else is neither");
    }

    [EditorTest(Category = "Tags", Thread = TestThread.Background)]
    public static void Exclude_beats_include()
    {
        var filter = new ObjectTargetFilter();
        filter.Set(EntityTagSet.From([1]), EntityTagSet.From([2]), includeUntagged: false);

        Assert.IsTrue(filter.Targets(Tagged(1)));
        Assert.IsFalse(filter.Targets(Tagged(1, 2)));

        filter.Set(EntityTagSet.Empty, EntityTagSet.From([2]), includeUntagged: false);
        Assert.IsTrue(filter.Targets(Tagged()));
        Assert.IsFalse(filter.Targets(Tagged(2)));
    }

    [EditorTest(Category = "Tags", Thread = TestThread.Background)]
    public static void Derived_entities_are_not_targetable_while_a_constraint_is_active()
    {
        var filter = new ObjectTargetFilter();
        var terrain = new DerivedEntity();

        filter.Set(EntityTagSet.From([1]), EntityTagSet.Empty, includeUntagged: true);
        Assert.IsFalse(filter.Targets(terrain));

        filter.Set(EntityTagSet.Empty, EntityTagSet.From([1]), includeUntagged: false);
        Assert.IsFalse(filter.Targets(terrain));

        filter.Set(EntityTagSet.Empty, EntityTagSet.Empty, includeUntagged: true);
        Assert.IsTrue(filter.Targets(terrain), "with nothing constrained the terrain answers clicks as before");
    }

    [EditorTest(Category = "Tags", Thread = TestThread.Background)]
    public static void Setting_the_same_filter_is_not_a_change()
    {
        var filter = new ObjectTargetFilter();
        filter.Set(EntityTagSet.From([1]), EntityTagSet.Empty, includeUntagged: false);
        int version = filter.Version;

        Assert.IsFalse(filter.Set(EntityTagSet.From([1]), EntityTagSet.Empty, includeUntagged: false));
        Assert.AreEqual(version, filter.Version);
    }

    [EditorTest(Category = "Tags", Thread = TestThread.Main)]
    public static void Changing_the_filter_prunes_the_selection()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_target_filter_test__" });
        var town = Tagged(1);
        var other = Tagged(2);
        context.Scene.Add(town);
        context.Scene.Add(other);
        context.Selection.Add(town);
        context.Selection.Add(other);

        context.Tags.SetObjectFilter(EntityTagSet.From([1]), EntityTagSet.Empty, includeUntagged: false);

        Assert.IsTrue(context.Selection.Selected.SequenceEqual([town]), "only the still-targetable entity stays selected");
    }

    [EditorTest(Category = "Tags", Thread = TestThread.Main)]
    public static void Targetable_follows_the_filter_and_tag_changes()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_target_filter_test__" });
        var entity = Tagged();
        context.Scene.Add(entity);
        var selection = new ObjectSelection(context.Selection, context.ViewCategories, context.Scene, context.Tags.ObjectFilter);

        Assert.IsTrue(selection.Targetable.Contains(entity));

        context.Tags.SetObjectFilter(EntityTagSet.From([7]), EntityTagSet.Empty, includeUntagged: false);
        Assert.IsFalse(selection.Targetable.Contains(entity));

        context.Scene.SetTags(entity, EntityTagSet.From([7]));
        Assert.IsTrue(selection.Targetable.Contains(entity), "tagging the entity brings it into the filter");
    }
}
