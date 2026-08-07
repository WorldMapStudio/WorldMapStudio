using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// The scene entities currently loaded into the editor. Entities are streamed in and out of here
/// (just the viewport's demo set for now); the outline lists them and the viewport picks against
/// them. <see cref="Version"/> bumps on add/remove so views can tell when to refresh.
/// </summary>
public sealed class SceneEntityRegistry
{
    private readonly List<SceneEntity> _entities = [];

    public IReadOnlyList<SceneEntity> Entities => _entities;

    public int Version { get; private set; }

    public void Add(SceneEntity entity)
    {
        _entities.Add(entity);
        Version++;
    }

    public bool Remove(SceneEntity entity)
    {
        if (!_entities.Remove(entity))
        {
            return false;
        }

        Version++;
        return true;
    }
}
