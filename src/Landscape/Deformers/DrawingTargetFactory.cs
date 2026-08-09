using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>EF Core row backing a <see cref="DrawingTargetEntity"/> in the Editor storage.</summary>
public sealed class DrawingTargetRecord
{
    public int Id { get; set; }

    public int MapId { get; set; }

    public string Name { get; set; } = "Drawing Target";

    public double PosX { get; set; }

    public double PosY { get; set; }

    public double PosZ { get; set; }

    public double BasisXX { get; set; } = 1.0;

    public double BasisXY { get; set; }

    public double BasisXZ { get; set; }

    public double BasisYX { get; set; }

    public double BasisYY { get; set; } = 1.0;

    public double BasisYZ { get; set; }

    public double BasisZX { get; set; }

    public double BasisZY { get; set; }

    public double BasisZZ { get; set; } = 1.0;

    public double MinX { get; set; }

    public double MinY { get; set; }

    public double MinZ { get; set; }

    public double MaxX { get; set; }

    public double MaxY { get; set; }

    public double MaxZ { get; set; }

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
}

/// <summary>Maps <see cref="DrawingTargetEntity"/> to and from the Editor storage's <c>landscape_drawing_targets</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class DrawingTargetFactory : ISceneEntityFactory
{
    private readonly EditorStorage _storage;

    public DrawingTargetFactory(EditorStorage storage)
    {
        _storage = storage;
    }

    public float Priority => 0.0f;

    public bool Handles(IEntity entity) => entity is DrawingTargetEntity;

    public long? PersistentKey(SceneEntity entity) => ((DrawingTargetEntity)entity).RecordId;

    public async Task<IReadOnlyList<SceneEntity>> ScanAsync(MapId map, Aabb region)
    {
        Vector3 min = region.Position;
        Vector3 max = region.End;

        await using EditorDbContext context = _storage.CreateContext();
        List<DrawingTargetRecord> rows = await context.LandscapeDrawingTargets.AsNoTracking()
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
        var target = (DrawingTargetEntity)entity;

        var record = new DrawingTargetRecord();
        if (target.RecordId is int id)
        {
            record.Id = id;
        }

        WriteRecord(target, record);
        if (target.RecordId is null)
        {
            db.LandscapeDrawingTargets.Add(record);
        }
        else
        {
            db.LandscapeDrawingTargets.Update(record);
        }

        return () => target.RecordId = record.Id;
    }

    public void StageDelete(DbContext context, IEntity entity)
    {
        var db = (EditorDbContext)context;
        if (((DrawingTargetEntity)entity).RecordId is int id)
        {
            db.LandscapeDrawingTargets.Remove(new DrawingTargetRecord { Id = id });
        }
    }

    private static DrawingTargetEntity ToEntity(DrawingTargetRecord record)
    {
        var target = new DrawingTargetEntity
        {
            RecordId = record.Id,
            Map = new MapId(record.MapId),
            Name = record.Name,
            WorldSizeX = (float)record.WorldSizeX,
            WorldSizeZ = (float)record.WorldSizeZ,
            Strength = (float)record.Strength,
            Channel = record.Channel,
            LayerId = record.LayerId,
            MaterialId = record.MaterialId,
            Priority = record.Priority,
        };

        target.LoadPixels(record.Width, record.Height, record.Pixels);
        target.Transform = new Transform3D(
            new Basis(
                new Vector3((float)record.BasisXX, (float)record.BasisXY, (float)record.BasisXZ),
                new Vector3((float)record.BasisYX, (float)record.BasisYY, (float)record.BasisYZ),
                new Vector3((float)record.BasisZX, (float)record.BasisZY, (float)record.BasisZZ)),
            new Vector3((float)record.PosX, (float)record.PosY, (float)record.PosZ));
        return target;
    }

    private static void WriteRecord(DrawingTargetEntity target, DrawingTargetRecord record)
    {
        Vector3 origin = target.Transform.Origin;
        Basis basis = target.Transform.Basis;
        Aabb bounds = target.WorldBounds;

        record.Name = target.Name;
        record.MapId = target.Map.Value;
        record.PosX = origin.X;
        record.PosY = origin.Y;
        record.PosZ = origin.Z;
        record.BasisXX = basis.X.X;
        record.BasisXY = basis.X.Y;
        record.BasisXZ = basis.X.Z;
        record.BasisYX = basis.Y.X;
        record.BasisYY = basis.Y.Y;
        record.BasisYZ = basis.Y.Z;
        record.BasisZX = basis.Z.X;
        record.BasisZY = basis.Z.Y;
        record.BasisZZ = basis.Z.Z;
        record.MinX = bounds.Position.X;
        record.MinY = bounds.Position.Y;
        record.MinZ = bounds.Position.Z;
        record.MaxX = bounds.End.X;
        record.MaxY = bounds.End.Y;
        record.MaxZ = bounds.End.Z;
        record.Width = target.Width;
        record.Height = target.Height;
        record.WorldSizeX = target.WorldSizeX;
        record.WorldSizeZ = target.WorldSizeZ;
        record.Strength = target.Strength;
        record.Channel = target.Channel;
        record.LayerId = target.LayerId;
        record.MaterialId = target.MaterialId;
        record.Priority = target.Priority;
        record.Pixels = target.CopyPixels();
    }
}
