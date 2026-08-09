using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// The catalog entities currently loaded into the editor. Unlike the scene registry, nothing streams
/// into this: the system that owns a catalog loads it through
/// <see cref="DatabaseSystem.LoadCatalog{TEntity}"/> and drops it when done.
///
/// Membership is also what tells a commit a save from a delete — a pinned entity still registered is
/// saved, one that has left is deleted — which is the same rule the scene registry provides for
/// scene entities. <see cref="Version"/> bumps on add/remove so views can tell when to refresh.
/// </summary>
public sealed class CatalogEntityRegistry
{
    private readonly List<CatalogEntity> _entities = [];

    public IReadOnlyList<CatalogEntity> Entities => _entities;

    public int Version { get; private set; }

    public bool Contains(CatalogEntity entity) => _entities.Contains(entity);

    /// <summary>The loaded entities of one catalog type, in load order.</summary>
    public IEnumerable<TEntity> OfType<TEntity>() where TEntity : CatalogEntity =>
        _entities.OfType<TEntity>();

    public void Add(CatalogEntity entity)
    {
        _entities.Add(entity);
        Version++;
    }

    public bool Remove(CatalogEntity entity)
    {
        if (!_entities.Remove(entity))
        {
            return false;
        }

        Version++;
        return true;
    }

    /// <summary>Drops every loaded entity of a type, e.g. before reloading it from storage.</summary>
    public void RemoveAll<TEntity>() where TEntity : CatalogEntity
    {
        if (_entities.RemoveAll(entity => entity is TEntity) > 0)
        {
            Version++;
        }
    }
}
