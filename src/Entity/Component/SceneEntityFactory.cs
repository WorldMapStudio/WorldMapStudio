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
}

public sealed class SceneMarkerComponentRecord
{
    public int EntityId { get; set; }

    public int Shape { get; set; }

    public SceneEntityRecord? Entity { get; set; }
}

public sealed class SceneStampComponentRecord
{
    public int EntityId { get; set; }

    public double Radius { get; set; } = 24.0;

    public double Falloff { get; set; } = 0.5;

    public double Strength { get; set; } = 1.0;

    public string Channel { get; set; } = "";

    public int? LayerId { get; set; }

    public int? MaterialId { get; set; }

    public int Priority { get; set; }

    public SceneEntityRecord? Entity { get; set; }
}

public sealed class SceneDrawingTargetComponentRecord
{
    public int EntityId { get; set; }

    public int Width { get; set; } = 256;

    public int Height { get; set; } = 256;

    public double WorldSizeX { get; set; } = 64.0;

    public double WorldSizeZ { get; set; } = 64.0;

    public double Strength { get; set; } = 1.0;

    public string Channel { get; set; } = "";

    public int? LayerId { get; set; }

    public int? MaterialId { get; set; }

    public int Priority { get; set; }

    public byte[] Pixels { get; set; } = [];

    public SceneEntityRecord? Entity { get; set; }
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

        int[] ids = rows.Select(row => row.Id).ToArray();
        Dictionary<int, SceneMarkerComponentRecord> markers = await context.SceneMarkerComponents.AsNoTracking()
            .Where(record => ids.Contains(record.EntityId))
            .ToDictionaryAsync(record => record.EntityId)
            .ConfigureAwait(false);
        Dictionary<int, SceneStampComponentRecord> stamps = await context.SceneStampComponents.AsNoTracking()
            .Where(record => ids.Contains(record.EntityId))
            .ToDictionaryAsync(record => record.EntityId)
            .ConfigureAwait(false);
        Dictionary<int, SceneDrawingTargetComponentRecord> drawingTargets = await context.SceneDrawingTargetComponents.AsNoTracking()
            .Where(record => ids.Contains(record.EntityId))
            .ToDictionaryAsync(record => record.EntityId)
            .ConfigureAwait(false);

        return rows.Select(row => ToEntity(row, markers, stamps, drawingTargets)).ToList();
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

        StageComponents(db, scene, record);
        return () => scene.RecordId = record.Id;
    }

    public void StageDelete(DbContext context, IEntity entity)
    {
        var db = (EditorDbContext)context;
        if (((SceneEntity)entity).RecordId is int id)
        {
            if (db.SceneMarkerComponents.Any(record => record.EntityId == id))
            {
                db.SceneMarkerComponents.Remove(new SceneMarkerComponentRecord { EntityId = id });
            }

            if (db.SceneStampComponents.Any(record => record.EntityId == id))
            {
                db.SceneStampComponents.Remove(new SceneStampComponentRecord { EntityId = id });
            }

            if (db.SceneDrawingTargetComponents.Any(record => record.EntityId == id))
            {
                db.SceneDrawingTargetComponents.Remove(new SceneDrawingTargetComponentRecord { EntityId = id });
            }

            db.SceneEntities.Remove(new SceneEntityRecord { Id = id });
        }
    }

    private static SceneEntity ToEntity(
        SceneEntityRecord record,
        IReadOnlyDictionary<int, SceneMarkerComponentRecord> markers,
        IReadOnlyDictionary<int, SceneStampComponentRecord> stamps,
        IReadOnlyDictionary<int, SceneDrawingTargetComponentRecord> drawingTargets)
    {
        var entity = new SceneEntity
        {
            RecordId = record.Id,
            Map = new MapId(record.MapId),
            Name = record.Name,
        };

        if (markers.TryGetValue(record.Id, out SceneMarkerComponentRecord? marker))
        {
            entity.LoadComponent(new MarkerComponent { Shape = (MarkerShape)marker.Shape });
        }

        if (stamps.TryGetValue(record.Id, out SceneStampComponentRecord? stamp))
        {
            entity.LoadComponent(new StampComponent
            {
                Radius = (float)stamp.Radius,
                Falloff = (float)stamp.Falloff,
                Strength = (float)stamp.Strength,
                Channel = stamp.Channel,
                LayerId = stamp.LayerId,
                MaterialId = stamp.MaterialId,
                Priority = stamp.Priority,
            });
        }

        if (drawingTargets.TryGetValue(record.Id, out SceneDrawingTargetComponentRecord? targetRecord))
        {
            var target = new DrawingTargetComponent
            {
                WorldSizeX = (float)targetRecord.WorldSizeX,
                WorldSizeZ = (float)targetRecord.WorldSizeZ,
                Strength = (float)targetRecord.Strength,
                Channel = targetRecord.Channel,
                LayerId = targetRecord.LayerId,
                MaterialId = targetRecord.MaterialId,
                Priority = targetRecord.Priority,
            };
            target.LoadPixels(targetRecord.Width, targetRecord.Height, targetRecord.Pixels);
            entity.LoadComponent(target);
        }

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

    private static void StageComponents(EditorDbContext db, SceneEntity entity, SceneEntityRecord record)
    {
        int? entityId = entity.RecordId;
        MarkerComponent? marker = entity.Component<MarkerComponent>();
        if (marker != null)
        {
            var row = new SceneMarkerComponentRecord
            {
                Entity = entityId is null ? record : null,
                EntityId = entityId ?? 0,
                Shape = (int)marker.Shape,
            };
            StageComponentRow(db.SceneMarkerComponents, row, entityId);
        }
        else
        {
            StageComponentDelete(db.SceneMarkerComponents, entityId);
        }

        StampComponent? stamp = entity.Component<StampComponent>();
        if (stamp != null)
        {
            var row = new SceneStampComponentRecord
            {
                Entity = entityId is null ? record : null,
                EntityId = entityId ?? 0,
                Radius = stamp.Radius,
                Falloff = stamp.Falloff,
                Strength = stamp.Strength,
                Channel = stamp.Channel,
                LayerId = stamp.LayerId,
                MaterialId = stamp.MaterialId,
                Priority = stamp.Priority,
            };
            StageComponentRow(db.SceneStampComponents, row, entityId);
        }
        else
        {
            StageComponentDelete(db.SceneStampComponents, entityId);
        }

        DrawingTargetComponent? target = entity.Component<DrawingTargetComponent>();
        if (target != null)
        {
            var row = new SceneDrawingTargetComponentRecord
            {
                Entity = entityId is null ? record : null,
                EntityId = entityId ?? 0,
                Width = target.Width,
                Height = target.Height,
                WorldSizeX = target.WorldSizeX,
                WorldSizeZ = target.WorldSizeZ,
                Strength = target.Strength,
                Channel = target.Channel,
                LayerId = target.LayerId,
                MaterialId = target.MaterialId,
                Priority = target.Priority,
                Pixels = target.CopyPixels(),
            };
            StageComponentRow(db.SceneDrawingTargetComponents, row, entityId);
        }
        else
        {
            StageComponentDelete(db.SceneDrawingTargetComponents, entityId);
        }
    }

    private static void StageComponentRow<TRecord>(DbSet<TRecord> set, TRecord row, int? entityId)
        where TRecord : class
    {
        if (entityId == null)
        {
            set.Add(row);
            return;
        }

        bool exists = set.Any(record => EF.Property<int>(record, nameof(SceneMarkerComponentRecord.EntityId)) == entityId.Value);
        if (exists)
        {
            set.Update(row);
        }
        else
        {
            set.Add(row);
        }
    }

    private static void StageComponentDelete<TRecord>(DbSet<TRecord> set, int? entityId)
        where TRecord : class, new()
    {
        if (entityId == null || !set.Any(record => EF.Property<int>(record, nameof(SceneMarkerComponentRecord.EntityId)) == entityId.Value))
        {
            return;
        }

        var row = new TRecord();
        typeof(TRecord).GetProperty(nameof(SceneMarkerComponentRecord.EntityId))!.SetValue(row, entityId.Value);
        set.Remove(row);
    }
}
