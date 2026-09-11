using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// The highest row id a catalog factory has stored, regardless of whether the factory is eager
/// (<see cref="ICatalogEntityFactory"/>) or lazy (<see cref="ILazyCatalogEntityFactory"/>) — a lazily
/// loaded catalog needs this exactly as much as an eagerly loaded one, since <see cref="CatalogEntityRegistry.AssignId{TEntity}"/>
/// seeds its high-water mark from it whether or not the type's rows are currently loaded.
/// </summary>
public interface IRecordIdSource
{
    /// <summary>The highest row id stored for this type, or 0 if none.</summary>
    Task<int> MaxRecordIdAsync();
}

/// <summary>
/// Persists a catalog entity type against its storage. Self-registers into a concrete storage with
/// [Subsystem(nameof(ThatStorage))], exactly like <see cref="ISceneEntityFactory"/>.
///
/// Catalog entities have no spatial extent, so there is no scan: the system that owns them asks
/// <see cref="DatabaseSystem.LoadCatalog{TEntity}"/> for the whole set when it needs them, and drops
/// them when it doesn't. Everything about saving is shared with scene entities
/// (see <see cref="IEntityFactory"/>), so one commit is one transaction across both kinds.
/// </summary>
public interface ICatalogEntityFactory : IEntityFactory, IRecordIdSource
{
    /// <summary>
    /// The entity type this factory produces, so a caller can ask for a catalog by type without
    /// already holding an instance to test with <see cref="IEntityFactory.Handles"/>.
    /// </summary>
    Type EntityType { get; }

    /// <summary>Loads every entity of this factory's type. Catalogs are small and loaded whole.</summary>
    Task<IReadOnlyList<CatalogEntity>> LoadAllAsync();
}
