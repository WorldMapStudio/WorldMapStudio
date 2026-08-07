using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// Persists a scene entity type against its storage and knows how to load its entities into the
/// scene. Self-registers into a concrete storage with [Subsystem(nameof(ThatStorage))]. The mapping
/// between our entities and the storage's EF Core rows is entirely the factory's business.
/// </summary>
public interface ISceneEntityFactory : ISubsystem
{
    /// <summary>Whether this factory owns the given entity.</summary>
    bool Handles(SceneEntity entity);

    /// <summary>Loads all of this factory's entities from the database.</summary>
    Task<IReadOnlyList<SceneEntity>> LoadAllAsync();

    /// <summary>Inserts or updates the given entity (must be one this factory produced).</summary>
    Task SaveAsync(SceneEntity entity);
}
