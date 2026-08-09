using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>EF Core row backing a <see cref="StampEntity"/> in the Editor storage.</summary>
public sealed class StampRecord
{
    public int Id { get; set; }

    public int MapId { get; set; }

    public string Name { get; set; } = "Stamp";

    public double PosX { get; set; }

    public double PosY { get; set; }

    public double PosZ { get; set; }

    public double MinX { get; set; }

    public double MinY { get; set; }

    public double MinZ { get; set; }

    public double MaxX { get; set; }

    public double MaxY { get; set; }

    public double MaxZ { get; set; }

    public double Radius { get; set; } = 24.0;

    public double Falloff { get; set; } = 0.5;

    public double Strength { get; set; } = 1.0;

    public string Channel { get; set; } = "";

    public int? TextureLayerId { get; set; }

    public int? HeightLayerId { get; set; }

    public int? MaterialId { get; set; }

    public int Priority { get; set; }
}

/// <summary>Maps <see cref="StampEntity"/> to and from the Editor storage's <c>landscape_stamps</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class StampFactory : ISceneEntityFactory
{
    private readonly EditorStorage _storage;

    public StampFactory(EditorStorage storage)
    {
        _storage = storage;
    }

    public float Priority => 0.0f;

    public bool Handles(IEntity entity) => entity is StampEntity;

    public long? PersistentKey(SceneEntity entity) => ((StampEntity)entity).RecordId;

    public async Task<IReadOnlyList<SceneEntity>> ScanAsync(MapId map, Aabb region)
    {
        Vector3 min = region.Position;
        Vector3 max = region.End;

        await using EditorDbContext context = _storage.CreateContext();
        List<StampRecord> rows = await context.LandscapeStamps.AsNoTracking()
            .Where(record => record.MapId == map.Value
                && record.MinX <= max.X && record.MaxX >= min.X
                && record.MinY <= max.Y && record.MaxY >= min.Y
                && record.MinZ <= max.Z && record.MaxZ >= min.Z)
            .ToListAsync()
            .ConfigureAwait(false);

        return rows.Select(ToEntity).ToList();
    }

    public Action Stage(DbContext context, IEntity entity)
    {
        var db = (EditorDbContext)context;
        var stamp = (StampEntity)entity;

        var record = new StampRecord();
        if (stamp.RecordId is int id)
        {
            record.Id = id;
        }

        WriteRecord(stamp, record);
        if (stamp.RecordId is null)
        {
            db.LandscapeStamps.Add(record);
        }
        else
        {
            db.LandscapeStamps.Update(record);
        }

        return () => stamp.RecordId = record.Id;
    }

    public void StageDelete(DbContext context, IEntity entity)
    {
        var db = (EditorDbContext)context;
        if (((StampEntity)entity).RecordId is int id)
        {
            db.LandscapeStamps.Remove(new StampRecord { Id = id });
        }
    }

    private static StampEntity ToEntity(StampRecord record)
    {
        var stamp = new StampEntity
        {
            RecordId = record.Id,
            Map = new MapId(record.MapId),
            Name = record.Name,
            Radius = (float)record.Radius,
            Falloff = (float)record.Falloff,
            Strength = (float)record.Strength,
            Channel = record.Channel,
            TextureLayerId = record.TextureLayerId,
            HeightLayerId = record.HeightLayerId,
            MaterialId = record.MaterialId,
            Priority = record.Priority,
        };

        stamp.Transform = new Transform3D(
            Basis.Identity,
            new Vector3((float)record.PosX, (float)record.PosY, (float)record.PosZ));
        return stamp;
    }

    private static void WriteRecord(StampEntity stamp, StampRecord record)
    {
        Vector3 origin = stamp.Transform.Origin;
        Aabb bounds = stamp.WorldBounds;

        record.Name = stamp.Name;
        record.MapId = stamp.Map.Value;
        record.PosX = origin.X;
        record.PosY = origin.Y;
        record.PosZ = origin.Z;
        record.MinX = bounds.Position.X;
        record.MinY = bounds.Position.Y;
        record.MinZ = bounds.Position.Z;
        record.MaxX = bounds.End.X;
        record.MaxY = bounds.End.Y;
        record.MaxZ = bounds.End.Z;
        record.Radius = stamp.Radius;
        record.Falloff = stamp.Falloff;
        record.Strength = stamp.Strength;
        record.Channel = stamp.Channel;
        record.TextureLayerId = stamp.TextureLayerId;
        record.HeightLayerId = stamp.HeightLayerId;
        record.MaterialId = stamp.MaterialId;
        record.Priority = stamp.Priority;
    }
}
