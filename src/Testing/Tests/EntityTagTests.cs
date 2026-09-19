using System.Linq;

namespace WorldMapStudio;

/// <summary>Covers <see cref="EntityTagSet"/> and the registry's tag mutation path.</summary>
public static class EntityTagTests
{
    [EditorTest(Category = "Tags", Thread = TestThread.Background)]
    public static void Adding_and_removing_a_tag_is_idempotent()
    {
        EntityTagSet set = EntityTagSet.Empty.With(5);

        Assert.IsTrue(set.With(5) == set, "adding a present tag changes nothing");
        Assert.IsTrue(set.Without(9) == set, "removing an absent tag changes nothing");
        Assert.IsTrue(set.Without(5).IsEmpty);
    }

    [EditorTest(Category = "Tags", Thread = TestThread.Background)]
    public static void Ids_stay_sorted_and_distinct()
    {
        EntityTagSet set = EntityTagSet.Empty.With(7).With(2).With(9).With(2);

        Assert.AreEqual("2,7,9", string.Join(",", set));
        Assert.AreEqual("2,7,9", string.Join(",", EntityTagSet.From([9, 2, 7, 7])));
        Assert.AreEqual("2,9", string.Join(",", set.Without(7)));
    }

    [EditorTest(Category = "Tags", Thread = TestThread.Background)]
    public static void Contains_holds_above_and_below_the_binary_search_threshold()
    {
        EntityTagSet large = EntityTagSet.From(Enumerable.Range(0, 40).Select(i => i * 3));

        Assert.IsTrue(large.Contains(39));
        Assert.IsFalse(large.Contains(40));
        Assert.IsTrue(EntityTagSet.From([4, 8]).Contains(8));
        Assert.IsFalse(EntityTagSet.From([4, 8]).Contains(5));
        Assert.IsFalse(EntityTagSet.Empty.Contains(0));
    }

    [EditorTest(Category = "Tags", Thread = TestThread.Background)]
    public static void Overlaps_needs_one_shared_tag()
    {
        EntityTagSet a = EntityTagSet.From([1, 4, 9]);

        Assert.IsTrue(a.Overlaps(EntityTagSet.From([2, 9])));
        Assert.IsFalse(a.Overlaps(EntityTagSet.From([2, 5, 10])));
        Assert.IsFalse(a.Overlaps(EntityTagSet.Empty));
        Assert.IsFalse(EntityTagSet.Empty.Overlaps(EntityTagSet.Empty));
    }

    [EditorTest(Category = "Tags", Thread = TestThread.Background)]
    public static void Sets_with_the_same_ids_are_equal_and_hash_alike()
    {
        EntityTagSet a = EntityTagSet.From([3, 1]);
        EntityTagSet b = EntityTagSet.Empty.With(1).With(3);

        Assert.IsTrue(a == b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.IsTrue(a != EntityTagSet.From([1]));
        Assert.IsTrue(EntityTagSet.Empty == EntityTagSet.From([]));
    }

    [EditorTest(Category = "Tags", Thread = TestThread.Background)]
    public static void SetTags_bumps_the_tag_version_only_on_a_change()
    {
        var scene = new SceneEntityRegistry();
        var entity = new MapSceneEntity();
        scene.Add(entity);
        int version = scene.TagVersion;

        scene.SetTags(entity, EntityTagSet.From([2]));
        Assert.Greater(scene.TagVersion, version);
        Assert.IsTrue(entity.Tags.Contains(2));

        version = scene.TagVersion;
        scene.SetTags(entity, EntityTagSet.From([2]));
        Assert.AreEqual(version, scene.TagVersion, "setting the same tags is not a change");
    }

    [EditorTest(Category = "Tags", Thread = TestThread.Background)]
    public static void Loading_with_tags_already_set_does_not_bump_the_tag_version()
    {
        var scene = new SceneEntityRegistry();
        int version = scene.TagVersion;

        var loaded = new MapSceneEntity { Tags = EntityTagSet.From([1]), PersistedTags = EntityTagSet.From([1]) };
        scene.Add(loaded);

        Assert.AreEqual(version, scene.TagVersion);
        Assert.IsTrue(loaded.Tags.Contains(1));
    }
}
