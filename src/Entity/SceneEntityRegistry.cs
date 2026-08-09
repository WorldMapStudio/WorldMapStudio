using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// The scene entities currently loaded into the editor. Entities are streamed in and out of here
/// (just the viewport's demo set for now); the outline lists them and the viewport picks against
/// them. <see cref="Version"/> bumps on add/remove so views can tell when to refresh.
/// </summary>
public sealed class SceneEntityRegistry
{
    private readonly List<SceneEntity> _entities = [];
    private readonly HashSet<SceneEntity> _peripheral = [];

    /// <summary>Everything loaded, including entities held only so derived data can be built.</summary>
    public IReadOnlyList<SceneEntity> Entities => _entities;

    /// <summary>
    /// The entities the user is actually looking at — everything loaded except the peripheral ones.
    /// This is what the outline lists and the viewport picks against.
    /// </summary>
    public IEnumerable<SceneEntity> InView => _entities.Where(entity => !_peripheral.Contains(entity));

    public int Version { get; private set; }

    public bool Contains(SceneEntity entity) => _entities.Contains(entity);

    /// <summary>
    /// Whether this entity is loaded only so derived data can be built correctly, rather than because
    /// the user is looking at it.
    ///
    /// Terrain at the edge of view is only right if the entities just past that edge are loaded — a
    /// stamp reaching in from outside shapes the chunk you can see. But those entities are not what
    /// you are looking at, so they are not drawn, listed or picked: they would be scenery you cannot
    /// reach the far side of, appearing and vanishing as the camera drifts.
    /// </summary>
    public bool IsPeripheral(SceneEntity entity) => _peripheral.Contains(entity);

    /// <summary>Marks whether an entity is loaded for evaluation only. Bumps the version on a change.</summary>
    public void SetPeripheral(SceneEntity entity, bool peripheral)
    {
        bool changed = peripheral ? _peripheral.Add(entity) : _peripheral.Remove(entity);
        if (changed)
        {
            Version++;
        }
    }

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

        _peripheral.Remove(entity);
        Version++;
        return true;
    }
}
