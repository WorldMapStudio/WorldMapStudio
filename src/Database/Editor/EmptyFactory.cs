using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>Maps <see cref="EmptyEntity"/> to and from the Editor storage's <c>empties</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class EmptyFactory : ISceneEntityFactory
{
    private readonly EditorStorage _storage;

    public float Priority => 0f;

    public EmptyFactory(EditorStorage storage)
    {
        _storage = storage;
    }

    public bool Handles(SceneEntity entity) => entity is EmptyEntity;

    public long? PersistentKey(SceneEntity entity) => ((EmptyEntity)entity).RecordId;

    public async Task<IReadOnlyList<SceneEntity>> ScanAsync(MapId map, Aabb region)
    {
        Vector3 min = region.Position;
        Vector3 max = region.End;

        await using EditorDbContext context = _storage.CreateContext();
        List<EmptyRecord> rows = await context.Empties.AsNoTracking()
            .Where(record => record.MapId == map.Value
                && record.PosX >= min.X && record.PosX <= max.X
                && record.PosY >= min.Y && record.PosY <= max.Y
                && record.PosZ >= min.Z && record.PosZ <= max.Z)
            .ToListAsync()
            .ConfigureAwait(false);
        return rows.Select(ToEntity).ToList();
    }

    public Action Stage(DbContext context, SceneEntity entity)
    {
        var db = (EditorDbContext)context;
        var empty = (EmptyEntity)entity;

        var record = new EmptyRecord();
        if (empty.RecordId is int id)
        {
            record.Id = id;
        }

        WriteRecord(empty, record);
        if (empty.RecordId is null)
        {
            db.Empties.Add(record);
        }
        else
        {
            db.Empties.Update(record);
        }

        // After SaveChanges, EF has populated the generated key on inserts; copy it back.
        return () => empty.RecordId = record.Id;
    }

    public void StageDelete(DbContext context, SceneEntity entity)
    {
        var db = (EditorDbContext)context;
        var empty = (EmptyEntity)entity;
        if (empty.RecordId is int id)
        {
            db.Empties.Remove(new EmptyRecord { Id = id });
        }
    }

    private static EmptyEntity ToEntity(EmptyRecord record)
    {
        var empty = new EmptyEntity
        {
            Name = record.Name,
            Shape = (EmptyShape)record.Shape,
            RecordId = record.Id,
            Map = new MapId(record.MapId),
        };

        var rotation = new Quaternion((float)record.RotX, (float)record.RotY, (float)record.RotZ, (float)record.RotW);
        var origin = new Vector3((float)record.PosX, (float)record.PosY, (float)record.PosZ);
        empty.Transform = new Transform3D(new Basis(rotation.Normalized()), origin);
        return empty;
    }

    private static void WriteRecord(EmptyEntity empty, EmptyRecord record)
    {
        Transform3D transform = empty.Transform;
        Quaternion rotation = transform.Basis.GetRotationQuaternion();

        record.Name = empty.Name;
        record.Shape = (int)empty.Shape;
        record.MapId = empty.Map.Value;
        record.PosX = transform.Origin.X;
        record.PosY = transform.Origin.Y;
        record.PosZ = transform.Origin.Z;
        record.RotX = rotation.X;
        record.RotY = rotation.Y;
        record.RotZ = rotation.Z;
        record.RotW = rotation.W;
    }
}
