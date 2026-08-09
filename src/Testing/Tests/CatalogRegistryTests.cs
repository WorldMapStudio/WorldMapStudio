using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Covers the catalog registry, whose membership is what tells a commit a save from a delete — the
/// same rule the scene registry provides for scene entities.
/// </summary>
public static class CatalogRegistryTests
{
    private sealed class Recipe : CatalogEntity
    {
        public string Title = "Recipe";

        public override string DisplayName => Title;
    }

    private sealed class Quest : CatalogEntity
    {
    }

    [EditorTest(Category = "Catalog", Thread = TestThread.Background)]
    public static void Registered_entities_are_found_by_type()
    {
        var registry = new CatalogEntityRegistry();
        var recipe = new Recipe();
        registry.Add(recipe);
        registry.Add(new Quest());

        Assert.AreEqual(2, registry.Entities.Count);
        Assert.AreEqual(1, registry.OfType<Recipe>().Count());
        Assert.IsTrue(registry.Contains(recipe));
    }

    [EditorTest(Category = "Catalog", Thread = TestThread.Background)]
    public static void Removing_leaves_the_entity_unregistered()
    {
        var registry = new CatalogEntityRegistry();
        var recipe = new Recipe();
        registry.Add(recipe);

        Assert.IsTrue(registry.Remove(recipe));
        Assert.IsFalse(registry.Contains(recipe), "an unregistered entity commits as a delete");
        Assert.IsFalse(registry.Remove(recipe), "removing twice is not an error but changes nothing");
    }

    [EditorTest(Category = "Catalog", Thread = TestThread.Background)]
    public static void Reloading_a_catalog_drops_only_its_own_type()
    {
        var registry = new CatalogEntityRegistry();
        registry.Add(new Recipe());
        registry.Add(new Recipe());
        var quest = new Quest();
        registry.Add(quest);

        registry.RemoveAll<Recipe>();

        Assert.AreEqual(1, registry.Entities.Count);
        Assert.IsTrue(registry.Contains(quest), "another catalog's entities survive a reload");
    }

    [EditorTest(Category = "Catalog", Thread = TestThread.Background)]
    public static void Version_bumps_only_on_real_changes()
    {
        var registry = new CatalogEntityRegistry();
        var recipe = new Recipe();

        registry.Add(recipe);
        int afterAdd = registry.Version;

        registry.RemoveAll<Quest>();
        Assert.AreEqual(afterAdd, registry.Version, "removing nothing should not invalidate views");

        registry.Remove(recipe);
        Assert.Greater(registry.Version, afterAdd);
    }
}
