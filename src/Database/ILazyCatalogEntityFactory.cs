using System;

namespace WorldMapStudio;

/// <summary>
/// Persists a catalog entity type against its storage, the same as <see cref="ICatalogEntityFactory"/>
/// (Stage/StageDelete/Configure/Handles, inherited from <see cref="IEntityFactory"/>) — but for a table
/// too large to load whole. Self-registers into a concrete storage with [Subsystem(nameof(ThatStorage))],
/// exactly like <see cref="ICatalogEntityFactory"/>.
///
/// Deliberately has no <c>LoadAllAsync</c>: there is no <see cref="DatabaseSystem.LoadCatalog{TEntity}"/>
/// equivalent for this interface; "load everything up front" is precisely what doesn't scale for this
/// shape of table. A concrete factory instead exposes its own on-demand lookup (by id, by search) that a
/// caller uses to pull one entity into <see cref="CatalogEntityRegistry"/> when it's actually opened for
/// editing — from that point it is undo/session/commit-tracked exactly like any other catalog entity,
/// and everything not opened never touches memory.
/// </summary>
public interface ILazyCatalogEntityFactory : IEntityFactory
{
    /// <summary>The entity type this factory produces.</summary>
    Type EntityType { get; }
}
