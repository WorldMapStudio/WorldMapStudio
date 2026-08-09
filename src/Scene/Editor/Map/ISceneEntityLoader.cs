using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Supplies scene entities to streaming without a storage behind them. An
/// <see cref="ISceneEntityFactory"/> answers "which rows are in this region"; a loader answers
/// "which entities are in this region", however it likes — landscape chunks are generated from the
/// grid, not read from a table.
///
/// If a persistent chunk cache ever lands, the chunk loader grows a storage behind it and nothing on
/// the streaming side changes.
/// </summary>
public interface ISceneEntityLoader
{
    /// <summary>
    /// How far beyond the view this loader needs <em>other</em> entities loaded in order to produce
    /// correct results at the view's edge, in world units.
    ///
    /// Landscape chunks are the reason this exists: a chunk on the edge of view is shaped by whatever
    /// overlaps it, and something overlapping it can easily sit outside the view. Without the margin,
    /// an edge chunk is built from whichever of its deformers happen to be loaded and quietly changes
    /// shape as you fly toward it.
    /// </summary>
    float LoadMargin => 0.0f;

    /// <summary>Whether this loader owns the given entity.</summary>
    bool Handles(SceneEntity entity);

    /// <summary>A stable key for the entity, so a re-scan does not duplicate what is already loaded.</summary>
    long KeyOf(SceneEntity entity);

    /// <summary>
    /// Called on the main thread immediately before a scan starts, so the loader can capture whatever
    /// live editor state it needs. <see cref="ScanAsync"/> may run on a background thread, where
    /// reading the scene registry or anything else the user is editing is a race.
    /// </summary>
    void Prepare() { }

    /// <summary>
    /// The entities of this loader that belong in the region, for the given map. This is the
    /// <em>view</em> region: a loader produces what the user should see, while stored entities are
    /// loaded over the wider region implied by <see cref="LoadMargin"/>.
    /// </summary>
    Task<IReadOnlyList<SceneEntity>> ScanAsync(MapId map, Aabb region);

    /// <summary>
    /// Folds a freshly scanned entity into the one already loaded under the same key, returning true
    /// if it did.
    ///
    /// For a stored entity, a re-scan finds the same row and the loaded copy simply wins. For derived
    /// content it is the opposite: a re-scan is a <em>rebuild</em>, and its result is the new truth —
    /// so a landscape chunk takes the new output rather than being discarded, keeping its identity
    /// (and the user's selection) while its content changes.
    /// </summary>
    bool TryRefresh(SceneEntity loaded, SceneEntity rescanned) => false;
}
