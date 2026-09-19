using Godot;

namespace WorldMapStudio;

/// <summary>
/// Covers <see cref="StreamingSystem.FateOf"/>: what happens to a loaded entity once the editor has
/// scanned somewhere.
///
/// These rules must not differ depending on whether an entity has ever been saved: "is it still loaded"
/// cannot be answered from a row id an unsaved entity does not have, or a new entity would never be
/// unloaded and would hang in the outline and the viewport over ground the editor had stopped loading.
/// What is asserted here is that place alone decides visibility, and that being saved is never part of
/// the question.
/// </summary>
public static class StreamingFateTests
{
    private static readonly MapId Open = new(1);
    private static readonly MapId Elsewhere = new(2);

    private const float ViewExtent = 160.0f;
    private const float Margin = 64.0f;

    private static Aabb View => Horizontal(Vector3.Zero, ViewExtent);

    private static Aabb Horizontal(Vector3 centre, float halfExtent) =>
        new(centre - new Vector3(halfExtent, 4096.0f, halfExtent),
            new Vector3(halfExtent * 2.0f, 8192.0f, halfExtent * 2.0f));

    // A one-unit entity standing at the given horizontal distance from the focus.
    private static Aabb At(float x) =>
        new(new Vector3(x - 0.5f, -0.5f, -0.5f), Vector3.One);

    private static StreamingSystem.EntityFate Fate(Aabb bounds, bool scanned, bool pinned, MapId? map = null) =>
        StreamingSystem.FateOf(map ?? Open, bounds, Open, View, scanned, pinned);

    [EditorTest(Category = "Streaming")]
    public static void An_entity_in_view_is_visible()
    {
        Assert.AreEqual(StreamingSystem.EntityFate.Visible, Fate(At(10.0f), scanned: true, pinned: false));
    }

    [EditorTest(Category = "Streaming")]
    public static void An_entity_in_the_margin_is_hidden_but_kept()
    {
        // The margin exists so terrain at the edge of view is built from everything that shapes it,
        // and stored entities are scanned over it — which is why the scan itself, rather than a second
        // geometric test, is what keeps them. They are inputs, not scenery: loaded, never shown.
        Assert.AreEqual(StreamingSystem.EntityFate.Hidden,
            Fate(At(ViewExtent + 10.0f), scanned: true, pinned: false));
    }

    [EditorTest(Category = "Streaming")]
    public static void Derived_content_the_loader_stopped_producing_unloads()
    {
        // Landscape chunks are produced for the view alone, so one that has left it is stale the
        // moment the loader stops returning it — nothing will ever refresh it again. Keeping it for
        // overlapping the margin, as a stored input is kept, would leave a ring of dead chunks behind
        // the camera.
        Assert.AreEqual(StreamingSystem.EntityFate.Unload,
            Fate(At(ViewExtent + 10.0f), scanned: false, pinned: false));
    }

    [EditorTest(Category = "Streaming")]
    public static void An_unheld_entity_out_of_range_unloads()
    {
        Assert.AreEqual(StreamingSystem.EntityFate.Unload,
            Fate(At(ViewExtent + Margin + 10.0f), scanned: false, pinned: false));
    }

    [EditorTest(Category = "Streaming")]
    public static void A_dirty_entity_out_of_range_is_kept_but_hidden()
    {
        // The whole point of pinning: the edit must survive until the session ends. But surviving is
        // not the same as being on screen — the chunk it stands on is gone, so it must not be drawn,
        // listed, picked, or left sitting in the selection.
        Assert.AreEqual(StreamingSystem.EntityFate.Hidden,
            Fate(At(ViewExtent + Margin + 10.0f), scanned: false, pinned: true));
    }

    [EditorTest(Category = "Streaming")]
    public static void A_new_entity_and_a_modified_one_share_a_fate()
    {
        // Having been saved shows up here as one thing only: a never-saved entity has no row, so no
        // scan can ever return it. Whether it did must not change the answer for a dirty entity, or
        // created entities would behave unlike edited ones.
        Aabb inRange = At(10.0f);
        Aabb outOfRange = At(ViewExtent + Margin + 10.0f);

        Assert.AreEqual(Fate(inRange, scanned: true, pinned: true), Fate(inRange, scanned: false, pinned: true));
        Assert.AreEqual(Fate(outOfRange, scanned: true, pinned: true), Fate(outOfRange, scanned: false, pinned: true));
    }

    [EditorTest(Category = "Streaming")]
    public static void A_new_entity_in_view_is_visible()
    {
        // Being absent from the scan must not cost an entity its visibility either: the user just
        // placed it in front of the camera.
        Assert.AreEqual(StreamingSystem.EntityFate.Visible, Fate(At(10.0f), scanned: false, pinned: true));
    }

    [EditorTest(Category = "Streaming")]
    public static void Committing_an_out_of_range_entity_unloads_it()
    {
        // Committing releases the pin, which is the only thing that was holding the entity. This is
        // what the editor was failing to notice until the user flew somewhere else.
        Aabb outOfRange = At(ViewExtent + Margin + 10.0f);

        Assert.AreEqual(StreamingSystem.EntityFate.Hidden, Fate(outOfRange, scanned: false, pinned: true));
        Assert.AreEqual(StreamingSystem.EntityFate.Unload, Fate(outOfRange, scanned: false, pinned: false));
    }

    [EditorTest(Category = "Streaming")]
    public static void Committing_an_in_range_entity_leaves_it_alone()
    {
        Assert.AreEqual(StreamingSystem.EntityFate.Visible, Fate(At(10.0f), scanned: false, pinned: false));
    }

    [EditorTest(Category = "Streaming")]
    public static void A_family_member_outside_the_region_is_kept()
    {
        // A scan reaches past its own region to complete parent/child families. Unloading what it
        // just handed back would have the two fighting: out one frame, in again the next.
        Assert.AreEqual(StreamingSystem.EntityFate.Hidden,
            Fate(At(ViewExtent + Margin + 500.0f), scanned: true, pinned: false));
    }

    [EditorTest(Category = "Streaming")]
    public static void Another_map_is_never_in_view()
    {
        // Overlapping the open map's view box means nothing when the entity is not in that map: map
        // ids are independent coordinate spaces.
        Assert.AreEqual(StreamingSystem.EntityFate.Unload,
            Fate(At(10.0f), scanned: false, pinned: false, map: Elsewhere));
    }

    [EditorTest(Category = "Streaming")]
    public static void A_dirty_entity_in_another_map_is_kept_but_hidden()
    {
        // Editing something, then switching maps, must not throw the edit away — nor show it here.
        Assert.AreEqual(StreamingSystem.EntityFate.Hidden,
            Fate(At(10.0f), scanned: false, pinned: true, map: Elsewhere));
    }

    [EditorTest(Category = "Streaming")]
    public static void Height_does_not_decide_visibility()
    {
        // Terrain is addressed on the ground plane, so flying high must not hide what is under you.
        var high = new Aabb(new Vector3(-0.5f, 900.0f, -0.5f), Vector3.One);

        Assert.AreEqual(StreamingSystem.EntityFate.Visible, Fate(high, scanned: true, pinned: false));
    }
}
