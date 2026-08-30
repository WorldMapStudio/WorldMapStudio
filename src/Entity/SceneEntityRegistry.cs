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
    private readonly HashSet<SceneEntity> _resident = [];

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

    /// <summary>
    /// Whether this entity is exempt from streaming — loaded once for the whole session and never
    /// unloaded by moving around the map.
    ///
    /// Everything else in here belongs to <see cref="StreamingSystem"/>, which decides what stays
    /// loaded from where the entity is; that is what lets an entity created in the editor unload on
    /// the same terms as one read from a table. Prefab templates cannot play by those rules: they sit
    /// on a reserved map no scan ever covers, so streaming would sweep them the first time it looked.
    /// </summary>
    public bool IsResident(SceneEntity entity) => _resident.Contains(entity);

    /// <summary>Marks an entity as exempt from streaming. Cleared when it leaves the registry.</summary>
    public void SetResident(SceneEntity entity, bool resident)
    {
        if (resident)
        {
            _resident.Add(entity);
        }
        else
        {
            _resident.Remove(entity);
        }
    }

    public void Add(SceneEntity entity)
    {
        _entities.Add(entity);
        Version++;
    }

    /// <summary>Drops every loaded entity. Does not tear down viewport representations — that is the
    /// viewport's own <see cref="IWorldParticipant.UnloadWorld"/>, since it is the one holding the
    /// Godot nodes.</summary>
    public void Clear()
    {
        if (_entities.Count == 0)
        {
            return;
        }

        _entities.Clear();
        _peripheral.Clear();
        _resident.Clear();
        Version++;
    }

    public bool Remove(SceneEntity entity)
    {
        if (!_entities.Remove(entity))
        {
            return false;
        }

        _peripheral.Remove(entity);
        _resident.Remove(entity);
        Version++;
        return true;
    }

    /// <summary>
    /// Signals that an already-loaded entity changed in place. View and terrain systems use the
    /// registry version as a cheap "something about the loaded scene moved" tick.
    /// </summary>
    public void Touch(SceneEntity entity)
    {
        if (_entities.Contains(entity))
        {
            Version++;
        }
    }
}
