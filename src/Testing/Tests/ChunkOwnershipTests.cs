using Godot;

namespace WorldMapStudio;

/// <summary>
/// Covers which components decide that a chunk exists. A map-spanning component is the case this
/// separation exists for: it needs enormous bounds so streaming keeps it loaded everywhere, and those
/// bounds must not be read as "there is terrain here" across the whole map.
/// </summary>
public static class ChunkOwnershipTests
{
    private sealed class BoxComponent(Aabb bounds, bool mapSpanning) : SceneComponent, ISceneBoundsProvider
    {
        public override string TypeId => "test-box";

        public override string DisplayName => "Test Box";

        public Aabb LocalBounds { get; } = bounds;

        public bool ContributesChunkOwnership { get; } = !mapSpanning;

        public override SceneComponent Clone() => new BoxComponent(LocalBounds, !ContributesChunkOwnership);
    }

    private static readonly Aabb Local = new(new Vector3(-8.0f, -1.0f, -8.0f), new Vector3(16.0f, 2.0f, 16.0f));
    private static readonly Aabb Global = new(Vector3.One * -1e6f, Vector3.One * 2e6f);

    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static void A_local_component_owns_the_chunks_it_covers()
    {
        var entity = new SceneEntity();
        entity.AddComponent(new BoxComponent(Local, mapSpanning: false));

        Assert.AreEqual(Local, entity.LocalChunkBounds);
    }

    /// <summary>The bug this exists to stop: a global light on the same entity as real content used to
    /// swallow that content's extent, so the entity claimed the whole map.</summary>
    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static void A_map_spanning_component_does_not_widen_what_an_entity_owns()
    {
        var entity = new SceneEntity();
        entity.AddComponent(new BoxComponent(Global, mapSpanning: true));
        entity.AddComponent(new BoxComponent(Local, mapSpanning: false));

        Assert.AreEqual(Global, entity.EffectiveLocalBounds, "streaming still sees the whole extent");
        Assert.AreEqual(Local, entity.LocalChunkBounds, "but only the local part owns chunks");
    }

    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static void An_entity_that_is_only_map_spanning_owns_nothing()
    {
        var entity = new SceneEntity();
        entity.AddComponent(new BoxComponent(Global, mapSpanning: true));

        Assert.IsNull(entity.LocalChunkBounds);
        Assert.IsNull(entity.WorldChunkBounds);
    }

    /// <summary>An entity with nothing to size it still sits somewhere, so it keeps the same unit-box
    /// fallback <see cref="SceneEntity.EffectiveLocalBounds"/> uses.</summary>
    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static void An_entity_with_no_bounds_components_still_owns_its_own_spot()
    {
        var entity = new SceneEntity();

        Assert.AreEqual(entity.EffectiveLocalBounds, entity.LocalChunkBounds);
    }

    /// <summary>
    /// A region far larger than the map must not be walked coordinate by coordinate. A global light's
    /// million-unit bounds used to make this yield the few thousand in-limit chunks out of billions of
    /// candidates, which reads as a hang rather than a slow loop.
    /// </summary>
    [EditorTest(Category = "ChunkChanges", Thread = TestThread.Background)]
    public static void Overlapping_a_map_spanning_region_stays_inside_the_limit()
    {
        var settings = new LandscapeSettings { ChunkWorldSize = 33.33f, ChunkLimit = 4 };
        var grid = new LandscapeGrid(settings);

        var coords = new System.Collections.Generic.List<ChunkCoord>(grid.Overlapping(Global));

        // (2 * 4 + 1) squared, and reached without walking the million units either side of it.
        Assert.AreEqual(81, coords.Count);
    }
}
