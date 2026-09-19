using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Persists a scene entity type against its storage and knows how to find its entities in space.
/// Self-registers into a concrete storage with [Subsystem(nameof(ThatStorage))]. The mapping between
/// our entities and the storage's EF Core rows is entirely the factory's business — see
/// <see cref="IEntityFactory"/> for the staging protocol.
/// </summary>
public interface ISceneEntityFactory : IEntityFactory
{
    /// <summary>
    /// The exact runtime type this factory's entities have. Must agree with
    /// <see cref="IEntityFactory.Handles"/>, so a caller can bucket loaded entities by factory
    /// without an instance to test.
    /// </summary>
    Type EntityType { get; }

    /// <summary>A stable key for the entity's persisted row, or null if it was never saved. Used to
    /// deduplicate streaming so a re-scan doesn't reload an entity that is already in the scene.</summary>
    long? PersistentKey(SceneEntity entity);

    /// <summary>
    /// Stable name under which this factory's entities are bridged into the editor's entity table so
    /// they can carry tags and attached components. Null opts out. Persisted; never rename.
    /// <see cref="PersistentKey"/> must be stable across sessions for a bridged factory: a key that is
    /// per-session or positional would attach editor data to whatever row happens to hold it next.
    /// </summary>
    string? BridgeSource => null;

    /// <summary>
    /// Loads this factory's entities for the given map whose <see cref="SceneEntity.WorldBounds"/>
    /// <em>overlap</em> the region. Overlap, not containment of the origin: a large building or a long
    /// spline influences a region its origin is nowhere near, and the landscape system asks this same
    /// question to find the entities that deform a chunk.
    ///
    /// <paramref name="loaded"/> is the keys of this factory's entities streaming already holds. The
    /// factory must not build those, but must still report them in <see cref="SceneEntityScan.Keys"/>.
    /// Callers with nothing loaded pass an empty set.
    ///
    /// <paramref name="publishing"/> says whether this scan's result is headed for
    /// <see cref="CatalogEntityRegistry"/> (streaming, the prefab library) or belongs only to the
    /// caller (an offline <see cref="DatabaseSystem.ScanSceneAsync"/> result) — see
    /// <see cref="SceneEntityScanCatalog.Publishing"/> for what that changes.
    /// </summary>
    Task<SceneEntityScan> ScanAsync(MapId map, Aabb region, IReadOnlySet<long> loaded, bool publishing);
}
