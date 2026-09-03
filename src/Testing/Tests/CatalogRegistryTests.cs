using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Covers the catalog registry, whose membership is what tells a commit a save from a delete — the
/// same rule the scene registry provides for scene entities.
/// </summary>
public static class CatalogRegistryTests
{
    private sealed class Recipe : CatalogEntity, IKeyedCatalogEntity
    {
        public string Title = "Recipe";

        public override string DisplayName => Title;

        public int? RecordId { get; set; }

        public bool IsSaved { get; set; }
    }

    private sealed class Quest : CatalogEntity, IKeyedCatalogEntity
    {
        public int? RecordId { get; set; }

        public bool IsSaved { get; set; }
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
    public static void Reloading_a_catalog_keeps_an_entity_the_keep_predicate_protects()
    {
        // Regression test: DatabaseSystem.LoadCatalog/UnloadCatalog pass a "still pinned by the active
        // edit session" predicate here so a type-scoped reload can never silently evict an uncommitted
        // create or edit — see the bug this fixed, where creating one catalog entity then another
        // dropped the first on commit because a reload in between wiped it from the registry while it
        // was still pinned.
        var registry = new CatalogEntityRegistry();
        var pinned = new Recipe();
        var unpinned = new Recipe();
        registry.Add(pinned);
        registry.Add(unpinned);

        registry.RemoveAll<Recipe>(entity => entity == pinned);

        Assert.AreEqual(1, registry.Entities.Count);
        Assert.IsTrue(registry.Contains(pinned), "a protected entity survives a type-scoped reload");
        Assert.IsFalse(registry.Contains(unpinned), "an unprotected entity is still dropped as normal");
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

    [EditorTest(Category = "Catalog", Thread = TestThread.Background)]
    public static void A_new_entity_is_identified_before_it_is_saved()
    {
        // The whole point: something created in the same session can reference it straight away,
        // instead of binding to null until a commit happens.
        var registry = new CatalogEntityRegistry();

        var first = new Recipe();
        registry.AssignId(first);
        registry.Add(first);
        Assert.IsNotNull(first.RecordId);
        Assert.IsFalse(first.IsSaved, "an id is not a row");

        var second = new Recipe();
        registry.AssignId(second);
        registry.Add(second);
        Assert.AreNotEqual(first.RecordId, second.RecordId);
    }

    [EditorTest(Category = "Catalog", Thread = TestThread.Background)]
    public static void Ids_continue_past_what_is_already_loaded()
    {
        var registry = new CatalogEntityRegistry();
        registry.Add(new Recipe { RecordId = 4, IsSaved = true });
        registry.Add(new Recipe { RecordId = 9, IsSaved = true });

        var created = new Recipe();
        registry.AssignId(created);

        Assert.AreEqual(10, created.RecordId, "reusing 5 would collide with the saved row 9 on commit");
    }

    [EditorTest(Category = "Catalog", Thread = TestThread.Background)]
    public static void Each_catalog_type_numbers_itself()
    {
        // Ids are row ids, and each catalog is its own table.
        var registry = new CatalogEntityRegistry();
        registry.Add(new Recipe { RecordId = 7, IsSaved = true });

        var quest = new Quest();
        registry.AssignId(quest);

        Assert.AreEqual(1, quest.RecordId);
    }
}
