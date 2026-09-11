using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// Persists a catalog entity type against its storage. Self-registers into a concrete storage with
/// [Subsystem(nameof(ThatStorage))], exactly like <see cref="ISceneEntityFactory"/>.
///
/// Catalog entities have no spatial extent, so there is no scan: the system that owns them asks
/// <see cref="DatabaseSystem.LoadCatalog{TEntity}"/> for the whole set when it needs them, and drops
/// them when it doesn't. Everything about saving is shared with scene entities
/// (see <see cref="IEntityFactory"/>), so one commit is one transaction across both kinds.
/// </summary>
public interface ICatalogEntityFactory : IEntityFactory
{
    /// <summary>
    /// The entity type this factory produces, so a caller can ask for a catalog by type without
    /// already holding an instance to test with <see cref="IEntityFactory.Handles"/>.
    /// </summary>
    Type EntityType { get; }

    /// <summary>Loads every entity of this factory's type. Catalogs are small and loaded whole.</summary>
    Task<IReadOnlyList<CatalogEntity>> LoadAllAsync();

    /// <summary>The highest row id stored for this type, or 0 if none. What
    /// <see cref="CatalogEntityRegistry.AssignId{TEntity}"/> seeds its high-water mark from, so a newly
    /// created entity never collides with a row that exists in storage but is not currently loaded.</summary>
    Task<int> MaxRecordIdAsync();
}
