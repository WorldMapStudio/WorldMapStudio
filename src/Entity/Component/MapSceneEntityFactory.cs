using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

public sealed class MapEntityRecord
{
    public int Id { get; set; }

    public int MapId { get; set; }

    public string Name { get; set; } = "Entity";

    public double PosX { get; set; }

    public double PosY { get; set; }

    public double PosZ { get; set; }

    public double RotX { get; set; }

    public double RotY { get; set; }

    public double RotZ { get; set; }

    public double RotW { get; set; } = 1.0;

    public double ScaleX { get; set; } = 1.0;

    public double ScaleY { get; set; } = 1.0;

    public double ScaleZ { get; set; } = 1.0;

    public double MinX { get; set; }

    public double MinY { get; set; }

    public double MinZ { get; set; }

    public double MaxX { get; set; }

    public double MaxY { get; set; }

    public double MaxZ { get; set; }
}

/// <summary>Named for readability at the many call sites below — see the type doc on the other part of
/// this partial class for why a table's DbSet lives beside its owner instead of being centrally listed.</summary>
public sealed partial class EditorDbContext
{
    public DbSet<MapEntityRecord> MapEntities => Set<MapEntityRecord>();
}

[Subsystem(nameof(EditorStorage))]
public sealed class MapSceneEntityFactory : ISceneEntityFactory, IMapScopedData
{
    private readonly EditorStorage _storage;

    // Set by PrepareBatchAsync, consumed by Stage: a high-water mark handed out sequentially to every
    // new entity in the current commit, so Pomelo can batch their inserts into one multi-row statement
    // instead of one INSERT + SELECT LAST_INSERT_ID() per row.
    private int _nextId;

    public MapSceneEntityFactory(EditorStorage storage)
    {
        _storage = storage;
    }

    public Type EntityType => typeof(SceneEntity);

    public bool Handles(IEntity entity) => entity.GetType() == typeof(SceneEntity);

    public void Configure(ModelBuilder model)
    {
        model.Entity<MapEntityRecord>(entity =>
        {
            entity.ToTable("wms_scene_entities");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Id).ValueGeneratedNever();

            // Serves the streaming region query; without it that query scans every map's rows.
            entity.HasIndex(record => new { record.MapId, record.MinX, record.MaxX, record.MinY, record.MaxY });
        });
    }

    public async Task PrepareBatchAsync(DbContext context, IReadOnlyList<IEntity> saves)
    {
        bool needsIds = saves.Any(entity => Handles(entity) && ((SceneEntity)entity).RecordId is null);
        if (!needsIds)
        {
            return;
        }

        var db = (EditorDbContext)context;
        _nextId = await db.MapEntities.AsNoTracking()
            .Select(record => (int?)record.Id)
            .MaxAsync()
            .ConfigureAwait(false) ?? 0;
    }

    public long? PersistentKey(SceneEntity entity) => entity.RecordId;

    public async Task<SceneEntityScan> ScanAsync(MapId map, Aabb region, IReadOnlySet<long> loaded, bool publishing)
    {
        Vector3 min = region.Position;
        Vector3 max = region.End;

        await using EditorDbContext context = _storage.CreateContext();

        long clock = DiagnosticLog.Start();
        List<int> inRegion = await context.MapEntities.AsNoTracking()
            .Where(record => record.MapId == map.Value
                && record.MinX <= max.X && record.MaxX >= min.X
                && record.MinY <= max.Y && record.MaxY >= min.Y
                && record.MinZ <= max.Z && record.MaxZ >= min.Z)
            .Select(record => record.Id)
            .ToListAsync()
            .ConfigureAwait(false);
        DiagnosticLog.Log($"    region query: {DiagnosticLog.MillisecondsSince(clock):F0}ms, {inRegion.Count} rows");

        long[] keys = inRegion.Select(id => (long)id).ToArray();
        int[] wanted = inRegion.Where(id => !loaded.Contains(id)).ToArray();
        if (wanted.Length == 0)
        {
            return new SceneEntityScan([], keys, []);
        }

        clock = DiagnosticLog.Start();
        List<MapEntityRecord> rows = await context.MapEntities.AsNoTracking()
            .Where(record => wanted.Contains(record.Id))
            .ToListAsync()
            .ConfigureAwait(false);
        DiagnosticLog.Log($"    row fetch: {DiagnosticLog.MillisecondsSince(clock):F0}ms, {rows.Count} rows");

        Dictionary<int, SceneEntity> entities = rows.ToDictionary(row => row.Id, ToEntity);

        var catalog = new SceneEntityScanCatalog(publishing);
        foreach (ISceneComponentPersistence persistence in _storage.ComponentPersistence)
        {
            using IDisposable scope = DiagnosticLog.Scope(persistence.GetType().Name);
            clock = DiagnosticLog.Start();
            await persistence.LoadAsync(context, entities, wanted, catalog).ConfigureAwait(false);
            DiagnosticLog.Log($"    {persistence.GetType().Name}: {DiagnosticLog.MillisecondsSince(clock):F0}ms");
        }

        return new SceneEntityScan(entities.Values.ToList(), keys, catalog.Loaded);
    }

    public Action Stage(DbContext context, IEntity entity)
    {
        var db = (EditorDbContext)context;
        var scene = (SceneEntity)entity;

        var record = new MapEntityRecord();
        if (scene.RecordId is int id)
        {
            record.Id = id;
        }
        else
        {
            // Client-assigned, like the record.Id ValueGeneratedNever() above expects — see
            // PrepareBatchAsync. scene.RecordId itself stays null until the write-back below runs:
            // component persistences key off it to decide new-vs-existing (EditorComponentPersistenceHelpers.StageRow),
            // and setting it early would flip them onto their slower existing-row path.
            record.Id = ++_nextId;
        }

        WriteRecord(scene, record);
        if (scene.RecordId is null)
        {
            db.MapEntities.Add(record);
        }
        else
        {
            db.MapEntities.Update(record);
        }

        foreach (ISceneComponentPersistence persistence in _storage.ComponentPersistence)
        {
            persistence.Stage(db, scene, record);
        }

        return () => scene.RecordId = record.Id;
    }

    public void StageDelete(DbContext context, IEntity entity)
    {
        var db = (EditorDbContext)context;
        if (((SceneEntity)entity).RecordId is int id)
        {
            foreach (ISceneComponentPersistence persistence in _storage.ComponentPersistence)
            {
                persistence.StageDelete(db, id);
            }

            db.MapEntities.Remove(new MapEntityRecord { Id = id });
        }
    }

    string IMapScopedData.Label => "Scene entities";

    Task<int> IMapScopedData.CountAsync(EditorDbContext context, MapId map) =>
        _storage.CountWhereMapAsync<MapEntityRecord>(context, nameof(MapEntityRecord.MapId), map);

    async Task<IReadOnlySet<int>> IMapScopedData.MapIdsAsync(EditorDbContext context) =>
        (await context.MapEntities.AsNoTracking().Select(record => record.MapId).Distinct().ToListAsync().ConfigureAwait(false))
            .ToHashSet();

    /// <summary>Fans every component persister's own map-scoped delete out first — mirroring
    /// <see cref="StageDelete(DbContext, IEntity)"/> — then deletes the entity rows themselves. Runs
    /// last among <see cref="IMapScopedData"/> owners of priority 0 or lower, so every owner that finds
    /// its rows through these entities still can.</summary>
    async Task IMapScopedData.DeleteAsync(EditorDbContext context, DbTransaction transaction, MapId map)
    {
        foreach (ISceneComponentPersistence persistence in _storage.ComponentPersistence)
        {
            await persistence.DeleteForMapAsync(context, transaction, map).ConfigureAwait(false);
        }

        await _storage.DeleteWhereMapAsync<MapEntityRecord>(context, transaction, nameof(MapEntityRecord.MapId), map).ConfigureAwait(false);
    }

    private static SceneEntity ToEntity(MapEntityRecord record)
    {
        var entity = new MapSceneEntity
        {
            RecordId = record.Id,
            Map = new MapId(record.MapId),
            Name = record.Name,
        };

        var rotation = new Quaternion((float)record.RotX, (float)record.RotY, (float)record.RotZ, (float)record.RotW);
        var origin = new Vector3((float)record.PosX, (float)record.PosY, (float)record.PosZ);
        var scale = new Vector3((float)record.ScaleX, (float)record.ScaleY, (float)record.ScaleZ);
        entity.Transform = new Transform3D(new Basis(rotation.Normalized()).ScaledLocal(scale), origin);
        return entity;
    }

    private static void WriteRecord(SceneEntity entity, MapEntityRecord record)
    {
        Transform3D transform = entity.Transform;
        Quaternion rotation = transform.Basis.GetRotationQuaternion();
        Vector3 scale = transform.Basis.Scale;
        Aabb bounds = entity.WorldBounds;

        record.Name = entity.Name;
        record.MapId = entity.Map.Value;
        record.PosX = transform.Origin.X;
        record.PosY = transform.Origin.Y;
        record.PosZ = transform.Origin.Z;
        record.RotX = rotation.X;
        record.RotY = rotation.Y;
        record.RotZ = rotation.Z;
        record.RotW = rotation.W;
        record.ScaleX = scale.X;
        record.ScaleY = scale.Y;
        record.ScaleZ = scale.Z;
        record.MinX = bounds.Position.X;
        record.MinY = bounds.Position.Y;
        record.MinZ = bounds.Position.Z;
        record.MaxX = bounds.End.X;
        record.MaxY = bounds.End.Y;
        record.MaxZ = bounds.End.Z;
    }
}
