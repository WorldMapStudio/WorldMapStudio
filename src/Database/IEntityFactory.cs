using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

    /// <summary>
    /// Runs once per commit, before any of this factory's entities are staged, for a batch-wide fact
    /// a factory needs — e.g. a high-water mark for ids it will hand out itself in <see cref="Stage"/>,
    /// fetched once instead of once per entity. <paramref name="saves"/> is every entity in the commit,
    /// not just this factory's; a factory that needs this filters with <see cref="Handles"/> itself.
    /// Most factories don't need this.
    /// </summary>
    Task PrepareBatchAsync(DbContext context, IReadOnlyList<IEntity> saves) => Task.CompletedTask;

    /// <summary>
    /// Declares this factory's table(s) into the storage's EF model, for a factory whose storage uses
    /// EF Core. Most core factories don't need this — their tables are already declared directly in
    /// that storage's <c>DbContext</c> — but a plugin factory targeting <see cref="EditorStorage"/> has
    /// no other way to contribute without <see cref="EditorStorage"/> knowing about it. Mirrors
    /// <see cref="ISceneComponentPersistence.Configure"/> on the component side.
    /// </summary>
    void Configure(ModelBuilder model) { }
}
