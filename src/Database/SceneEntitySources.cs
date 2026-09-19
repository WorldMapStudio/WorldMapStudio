using System;
using System.Collections.Concurrent;

namespace WorldMapStudio;

/// <summary>
/// Answers "which storage and factory own this scene entity" from its runtime type, cached. The
/// question is asked per entity per commit and per streaming pass, and the alternative is a linear scan
/// of every factory in every storage for each one. Subsystems are fixed after startup, so an answer
/// never goes stale.
/// </summary>
public sealed class SceneEntitySources(DatabaseSystem database)
{
    private readonly record struct Owner(Storage Storage, ISceneEntityFactory Factory);

    private readonly ConcurrentDictionary<Type, Owner?> _owners = new();

    /// <summary>The factory that persists <paramref name="entity"/>, or null when nothing does (a derived
    /// entity, or a kind no factory has been registered for).</summary>
    public ISceneEntityFactory? FactoryFor(SceneEntity entity) => OwnerOf(entity)?.Factory;

    /// <summary>The storage that holds <paramref name="entity"/>, or null when nothing does.</summary>
    public Storage? StorageOf(SceneEntity entity) => OwnerOf(entity)?.Storage;

    /// <summary>
    /// Where a bridged entity's own row lives: the owning factory's <see cref="ISceneEntityFactory.BridgeSource"/>
    /// and the row's key, which is null until that row exists. Null for an entity that isn't bridged.
    /// </summary>
    public (string Source, long? Key)? SourceOf(SceneEntity entity) =>
        OwnerOf(entity) is { Factory: { BridgeSource: { } source } factory }
            ? (source, factory.PersistentKey(entity))
            : null;

    private Owner? OwnerOf(SceneEntity entity) =>
        _owners.GetOrAdd(entity.GetType(), _ => Find(entity));

    private Owner? Find(SceneEntity entity)
    {
        foreach (Storage storage in database.Storages)
        {
            foreach (ISceneEntityFactory factory in storage.SceneFactories)
            {
                if (factory.Handles(entity))
                {
                    return new Owner(storage, factory);
                }
            }
        }

        return null;
    }
}
