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
    /// <summary>Whether this loader owns the given entity.</summary>
    bool Handles(SceneEntity entity);

    /// <summary>A stable key for the entity, so a re-scan does not duplicate what is already loaded.</summary>
    long KeyOf(SceneEntity entity);

    /// <summary>The entities of this loader that belong in the region, for the given map.</summary>
    Task<IReadOnlyList<SceneEntity>> ScanAsync(MapId map, Aabb region);
}
