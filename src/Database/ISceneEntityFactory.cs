using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>
/// Persists a scene entity type against its storage and knows how to load its entities into the
/// scene. Self-registers into a concrete storage with [Subsystem(nameof(ThatStorage))]. The mapping
/// between our entities and the storage's EF Core rows is entirely the factory's business.
///
/// Saves and deletes are <em>staged</em> into a context the storage owns, so a whole commit is one
/// transaction; the storage saves once and then runs the returned write-back callbacks.
/// </summary>
public interface ISceneEntityFactory : ISubsystem
{
    /// <summary>Whether this factory owns the given entity.</summary>
    bool Handles(SceneEntity entity);

    /// <summary>Loads all of this factory's entities from the database.</summary>
    Task<IReadOnlyList<SceneEntity>> LoadAllAsync();

    /// <summary>
    /// Stages an insert or update of the entity into the storage's context, returning a callback to
    /// run after the changes are saved (e.g. to copy a generated key back onto the entity).
    /// </summary>
    Action Stage(DbContext context, SceneEntity entity);

    /// <summary>Stages a delete of the entity, or does nothing if it was never persisted.</summary>
    void StageDelete(DbContext context, SceneEntity entity);
}
