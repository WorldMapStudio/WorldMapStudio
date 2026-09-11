using System;
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
    private readonly HashSet<CatalogEntity> _entitySet = [];
    private readonly Dictionary<EntityId, CatalogEntity> _byId = [];
    private readonly Dictionary<Type, int> _highWaterMarks = [];

    private Func<Type, IRecordIdSource?>? _factoryLookup;

    public IReadOnlyList<CatalogEntity> Entities => _entities;

    public int Version { get; private set; }

    public bool Contains(CatalogEntity entity) => _entitySet.Contains(entity);

    /// <summary>Looks up a loaded entity by id in O(1), instead of scanning <see cref="Entities"/>.</summary>
    public CatalogEntity? Find(EntityId id) => _byId.GetValueOrDefault(id);

    /// <summary>The loaded entities of one catalog type, in load order.</summary>
    public IEnumerable<TEntity> OfType<TEntity>() where TEntity : CatalogEntity =>
        _entities.OfType<TEntity>();

    /// <summary>
    /// Bound once by <see cref="EditorContext"/> after <see cref="DatabaseSystem"/> exists, since this
    /// registry is constructed first. Lets <see cref="AssignId{TEntity}"/> seed a type's high-water mark
    /// from storage rather than assuming the loaded set is everything — required once a catalog can be
    /// lazily loaded, where most rows are never loaded at all. Typed against <see cref="IRecordIdSource"/>
    /// rather than <see cref="ICatalogEntityFactory"/> so a lazy factory's type answers this exactly the
    /// same way an eager one's does.
    /// </summary>
    public void BindFactoryLookup(Func<Type, IRecordIdSource?> lookup) => _factoryLookup = lookup;

    /// <summary>
    /// Gives a newly created entity the next free row id for its type, so other entities can
    /// reference it immediately rather than only after a commit. Call before adding it.
    ///
    /// Backed by a per-type high-water mark rather than a scan of the loaded set on every call: seeded
    /// once, from <c>max(loaded, stored)</c>, the first time a type is assigned, then only incremented —
    /// eviction never lowers it, so an id already handed out can never be reused. For an eagerly-loaded
    /// type the seed equals what the old per-call scan produced, so behaviour is unchanged; for a
    /// lazily-loaded type most rows are never loaded, so the stored MAX is the only thing that can
    /// answer this correctly.
    /// </summary>
    public void AssignId<TEntity>(TEntity entity) where TEntity : CatalogEntity, IKeyedCatalogEntity
    {
        Type type = typeof(TEntity);
        if (!_highWaterMarks.TryGetValue(type, out int mark))
        {
            mark = SeedHighWaterMark<TEntity>(type);
        }

        mark++;
        _highWaterMarks[type] = mark;
        entity.RecordId = mark;
    }

    /// <summary>Peeks the id <see cref="AssignId{TEntity}"/> would hand out next, without reserving it —
    /// unlike the old scan-based AssignId, calling AssignId itself to peek would now advance the
    /// high-water mark and skip an id, so a caller that wants to pre-fill a form uses this instead.</summary>
    public int PeekNextId<TEntity>() where TEntity : CatalogEntity, IKeyedCatalogEntity
    {
        Type type = typeof(TEntity);
        if (!_highWaterMarks.TryGetValue(type, out int mark))
        {
            mark = SeedHighWaterMark<TEntity>(type);
            _highWaterMarks[type] = mark;
        }

        return mark + 1;
    }

    private int SeedHighWaterMark<TEntity>(Type type) where TEntity : CatalogEntity, IKeyedCatalogEntity
    {
        int seed = _factoryLookup?.Invoke(type) is { } factory ? BlockingWork.Run(factory.MaxRecordIdAsync) : 0;

        foreach (TEntity existing in OfType<TEntity>())
        {
            if (existing.RecordId is { } id && id > seed)
            {
                seed = id;
            }
        }

        return seed;
    }

    public void Add(CatalogEntity entity)
    {
        _entities.Add(entity);
        _entitySet.Add(entity);
        _byId[entity.Id] = entity;
        Version++;
    }

    public bool Remove(CatalogEntity entity)
    {
        if (!_entitySet.Remove(entity))
        {
            return false;
        }

        _entities.Remove(entity);
        _byId.Remove(entity.Id);
        Version++;
        return true;
    }

    /// <summary>
    /// Drops every loaded entity of a type, e.g. before reloading it from storage. An entity for which
    /// <paramref name="keep"/> returns true is left alone rather than dropped — used to protect a
    /// pinned-but-uncommitted entity from a type-scoped reload it has nothing to do with, the same way
    /// <c>StreamingSystem</c> protects a pinned scene entity from incidental streaming eviction.
    /// </summary>
    public void RemoveAll<TEntity>(Func<TEntity, bool>? keep = null) where TEntity : CatalogEntity
    {
        bool ShouldRemove(CatalogEntity entity) => entity is TEntity typed && keep?.Invoke(typed) != true;
        RemoveWhere(ShouldRemove);
    }

    /// <summary>Type-based counterpart of <see cref="RemoveAll{TEntity}"/>, for a caller that only has
    /// a runtime <see cref="Type"/> to filter by — e.g. looping every registered catalog factory's
    /// <see cref="ICatalogEntityFactory.EntityType"/> rather than naming each concrete type.</summary>
    public void RemoveAll(Type entityType, Func<CatalogEntity, bool>? keep = null)
    {
        bool ShouldRemove(CatalogEntity entity) => entityType.IsInstanceOfType(entity) && keep?.Invoke(entity) != true;
        RemoveWhere(ShouldRemove);
    }

    private void RemoveWhere(Func<CatalogEntity, bool> shouldRemove)
    {
        List<CatalogEntity> removed = _entities.Where(shouldRemove).ToList();
        if (removed.Count == 0)
        {
            return;
        }

        foreach (CatalogEntity entity in removed)
        {
            _entities.Remove(entity);
            _entitySet.Remove(entity);
            _byId.Remove(entity.Id);
        }

        Version++;
    }

    /// <summary>Drops every loaded entity of every type.</summary>
    public void Clear()
    {
        if (_entities.Count == 0)
        {
            return;
        }

        _entities.Clear();
        _entitySet.Clear();
        _byId.Clear();
        Version++;
    }
}
