using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Streams scene entities in and out as the editor's focus moves. Each update it scans the current
/// map's storages for entities within a box around the focus on a background thread, then on the main
/// thread reconciles the scene registry: newly in-range entities are added, and streamed entities
/// that have left the range are removed unless the active edit session still pins them. Runs one scan
/// at a time and only re-scans once the focus has moved far enough (or the map changed).
/// </summary>
public sealed class StreamingSystem
{
    private const float Range = 60.0f;          // half-extent of the load box
    private const float RescanDistance = 15.0f; // focus travel before a re-scan

    private readonly EditorContext _context;
    private readonly Dictionary<(Type Type, long Key), SceneEntity> _streamed = new();

    private Task<List<SceneEntity>>? _pendingScan;
    private MapId _scanMap;
    private Vector3 _lastFocus;
    private bool _scanned;

    public StreamingSystem(EditorContext context)
    {
        _context = context;
    }

    /// <summary>Called each frame with the viewport focus (camera position, in Godot space).</summary>
    public void Update(Vector3 focus)
    {
        ApplyCompletedScan();

        if (_pendingScan != null)
        {
            return;
        }

        MapId map = _context.Maps.CurrentMap;
        bool mapChanged = !_scanned || !map.Equals(_scanMap);
        if (!mapChanged && focus.DistanceTo(_lastFocus) < RescanDistance)
        {
            return;
        }

        _scanned = true;
        _scanMap = map;
        _lastFocus = focus;

        var extent = new Vector3(Range, Range, Range);
        var region = new Aabb(focus - extent, extent * 2.0f);
        _pendingScan = ScanAsync(map, region);
    }

    private void ApplyCompletedScan()
    {
        if (_pendingScan is not { IsCompleted: true })
        {
            return;
        }

        Task<List<SceneEntity>> scan = _pendingScan;
        _pendingScan = null;

        if (!scan.IsCompletedSuccessfully)
        {
            GD.PushError($"[Streaming] Scan failed: {scan.Exception?.GetBaseException().Message}");
            return;
        }

        Reconcile(scan.Result);
    }

    private async Task<List<SceneEntity>> ScanAsync(MapId map, Aabb region)
    {
        var result = new List<SceneEntity>();
        foreach (Storage storage in _context.Database.Storages)
        {
            foreach (ISceneEntityFactory factory in storage.SceneFactories)
            {
                using IDisposable read = await storage.Lock.ReaderAsync().ConfigureAwait(false);
                IReadOnlyList<SceneEntity> scanned = await factory.ScanAsync(map, region).ConfigureAwait(false);
                result.AddRange(scanned);
            }
        }

        return result;
    }

    private void Reconcile(List<SceneEntity> scanned)
    {
        // Index entities already in the scene that carry a persistent key, so a scan never duplicates
        // one that was created-and-committed (and is therefore not yet in _streamed).
        var loaded = new Dictionary<(Type, long), SceneEntity>();
        foreach (SceneEntity entity in _context.Scene.Entities)
        {
            if (FactoryFor(entity)?.PersistentKey(entity) is long key)
            {
                loaded[(entity.GetType(), key)] = entity;
            }
        }

        var present = new HashSet<(Type, long)>();
        foreach (SceneEntity entity in scanned)
        {
            if (FactoryFor(entity)?.PersistentKey(entity) is not long key)
            {
                continue;
            }

            var id = (entity.GetType(), key);
            present.Add(id);

            if (loaded.TryGetValue(id, out SceneEntity? existing))
            {
                _streamed.TryAdd(id, existing);
            }
            else if (_streamed.TryAdd(id, entity))
            {
                _context.Scene.Add(entity);
            }
        }

        // Unload streamed entities that fell out of range, unless the session still holds them.
        var stale = new List<(Type, long)>();
        foreach (KeyValuePair<(Type, long), SceneEntity> pair in _streamed)
        {
            if (present.Contains(pair.Key) || IsPinned(pair.Value))
            {
                continue;
            }

            _context.Scene.Remove(pair.Value);
            stale.Add(pair.Key);
        }

        foreach ((Type, long) id in stale)
        {
            _streamed.Remove(id);
        }
    }

    private bool IsPinned(SceneEntity entity)
    {
        foreach (IEntity pinned in _context.EditSessions.Active.Pinned)
        {
            if (ReferenceEquals(pinned, entity))
            {
                return true;
            }
        }

        return false;
    }

    private ISceneEntityFactory? FactoryFor(SceneEntity entity)
    {
        foreach (Storage storage in _context.Database.Storages)
        {
            foreach (ISceneEntityFactory factory in storage.SceneFactories)
            {
                if (factory.Handles(entity))
                {
                    return factory;
                }
            }
        }

        return null;
    }
}
