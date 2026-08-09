using System;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>
/// The persistence half of an entity type: how it maps to and from its storage's EF Core rows.
/// Shared by <see cref="ISceneEntityFactory"/> and <see cref="ICatalogEntityFactory"/>, which differ
/// only in how their entities are <em>found</em> — scene entities by spatial scan, catalog entities
/// by explicit load.
///
/// Saves and deletes are <em>staged</em> into a context the storage owns, so a whole commit is one
/// transaction across every entity kind the session touched; the storage saves once and then runs the
/// returned write-back callbacks.
/// </summary>
public interface IEntityFactory : ISubsystem
{
    /// <summary>Whether this factory owns the given entity.</summary>
    bool Handles(IEntity entity);

    /// <summary>
    /// Stages an insert or update of the entity into the storage's context, returning a callback to
    /// run after the changes are saved (e.g. to copy a generated key back onto the entity).
    /// </summary>
    Action Stage(DbContext context, IEntity entity);

    /// <summary>Stages a delete of the entity, or does nothing if it was never persisted.</summary>
    void StageDelete(DbContext context, IEntity entity);
}
