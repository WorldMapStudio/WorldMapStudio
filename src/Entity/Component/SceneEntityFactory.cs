using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

public sealed class SceneEntityRecord
{
    public int Id { get; set; }

    public int? ParentId { get; set; }

    public int MapId { get; set; }

    public string Name { get; set; } = "Entity";

    public double PosX { get; set; }

    public double PosY { get; set; }

    public double PosZ { get; set; }

    public double RotX { get; set; }

    public double RotY { get; set; }

    public double RotZ { get; set; }

    public double RotW { get; set; } = 1.0;

    public double MinX { get; set; }

    public double MinY { get; set; }

    public double MinZ { get; set; }

    public double MaxX { get; set; }

    public double MaxY { get; set; }

    public double MaxZ { get; set; }

    public SceneEntityRecord? Parent { get; set; }
}

[Subsystem(nameof(EditorStorage))]
public sealed class SceneEntityFactory : ISceneEntityFactory
{
    private readonly EditorStorage _storage;

    public SceneEntityFactory(EditorStorage storage)
    {
        _storage = storage;
    }

    public float Priority => 0.0f;

    public bool Handles(IEntity entity) => entity.GetType() == typeof(SceneEntity);

    public long? PersistentKey(SceneEntity entity) => entity.RecordId;

    public async Task<IReadOnlyList<SceneEntity>> ScanAsync(MapId map, Aabb region)
    {
        Vector3 min = region.Position;
        Vector3 max = region.End;

        await using EditorDbContext context = _storage.CreateContext();
        List<SceneEntityRecord> rows = await context.SceneEntities.AsNoTracking()
            .Where(record => record.MapId == map.Value
                && record.MinX <= max.X && record.MaxX >= min.X
                && record.MinY <= max.Y && record.MaxY >= min.Y
                && record.MinZ <= max.Z && record.MaxZ >= min.Z)
            .ToListAsync()
            .ConfigureAwait(false);
        await ExpandRowsToFamiliesAsync(context, rows, map).ConfigureAwait(false);

        Dictionary<int, SceneEntity> entities = rows.ToDictionary(row => row.Id, ToEntity);
        int[] ids = rows.Select(row => row.Id).ToArray();

        foreach (ISceneComponentPersistence persistence in _storage.ComponentPersistence)
        {
            await persistence.LoadAsync(context, entities, ids).ConfigureAwait(false);
        }

        List<SceneEntity> result = entities.Values.ToList();
        LinkParents(result);
        return result;
    }

    public Action Stage(DbContext context, IEntity entity)
    {
        var db = (EditorDbContext)context;
        var scene = (SceneEntity)entity;

        var record = new SceneEntityRecord();
        if (scene.RecordId is int id)
        {
            record.Id = id;
        }

        WriteRecord(scene, record);
        if (scene.RecordId is null)
        {
            db.SceneEntities.Add(record);
        }
        else
        {
            db.SceneEntities.Update(record);
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

            db.SceneEntities.Remove(new SceneEntityRecord { Id = id });
        }
    }

    private static SceneEntity ToEntity(SceneEntityRecord record)
    {
        var entity = new SceneEntity
        {
            RecordId = record.Id,
            ParentRecordId = record.ParentId,
            Map = new MapId(record.MapId),
            Name = record.Name,
        };

        var rotation = new Quaternion((float)record.RotX, (float)record.RotY, (float)record.RotZ, (float)record.RotW);
        var origin = new Vector3((float)record.PosX, (float)record.PosY, (float)record.PosZ);
        entity.Transform = new Transform3D(new Basis(rotation.Normalized()), origin);
        return entity;
    }

    private static void WriteRecord(SceneEntity entity, SceneEntityRecord record)
    {
        Transform3D transform = entity.Transform;
        Quaternion rotation = transform.Basis.GetRotationQuaternion();
        Aabb bounds = entity.WorldBounds;

        record.Name = entity.Name;
        record.ParentId = entity.Parent?.RecordId ?? entity.ParentRecordId;
        record.MapId = entity.Map.Value;
        record.PosX = transform.Origin.X;
        record.PosY = transform.Origin.Y;
        record.PosZ = transform.Origin.Z;
        record.RotX = rotation.X;
        record.RotY = rotation.Y;
        record.RotZ = rotation.Z;
        record.RotW = rotation.W;
        record.MinX = bounds.Position.X;
        record.MinY = bounds.Position.Y;
        record.MinZ = bounds.Position.Z;
        record.MaxX = bounds.End.X;
        record.MaxY = bounds.End.Y;
        record.MaxZ = bounds.End.Z;
    }

    private static async Task ExpandRowsToFamiliesAsync(EditorDbContext context, List<SceneEntityRecord> rows, MapId map)
    {
        var byId = rows.ToDictionary(row => row.Id);
        bool changed;
        do
        {
            changed = false;

            int[] missingParents = rows
                .Select(row => row.ParentId)
                .Where(id => id is not null && !byId.ContainsKey(id.Value))
                .Select(id => id!.Value)
                .Distinct()
                .ToArray();
            if (missingParents.Length > 0)
            {
                List<SceneEntityRecord> parents = await context.SceneEntities.AsNoTracking()
                    .Where(record => record.MapId == map.Value && missingParents.Contains(record.Id))
                    .ToListAsync()
                    .ConfigureAwait(false);
                foreach (SceneEntityRecord parent in parents)
                {
                    if (byId.TryAdd(parent.Id, parent))
                    {
                        rows.Add(parent);
                        changed = true;
                    }
                }
            }

            int[] parentIds = byId.Keys.ToArray();
            List<SceneEntityRecord> children = await context.SceneEntities.AsNoTracking()
                .Where(record => record.MapId == map.Value
                    && record.ParentId != null
                    && parentIds.Contains(record.ParentId.Value)
                    && !parentIds.Contains(record.Id))
                .ToListAsync()
                .ConfigureAwait(false);
            foreach (SceneEntityRecord child in children)
            {
                if (byId.TryAdd(child.Id, child))
                {
                    rows.Add(child);
                    changed = true;
                }
            }
        }
        while (changed);
    }

    private static void LinkParents(IReadOnlyList<SceneEntity> entities)
    {
        var byRecordId = entities
            .Where(entity => entity.RecordId is not null)
            .ToDictionary(entity => entity.RecordId!.Value);
        foreach (SceneEntity entity in entities)
        {
            entity.Parent = entity.ParentRecordId is int parentId && byRecordId.TryGetValue(parentId, out SceneEntity? parent)
                ? parent
                : null;
        }
    }
}
