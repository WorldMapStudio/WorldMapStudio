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

    public async Task<IReadOnlyList<SceneEntity>> LoadAllAsync()
    {
        await using EditorDbContext context = _storage.CreateContext();
        List<EmptyRecord> rows = await context.Empties.AsNoTracking().ToListAsync().ConfigureAwait(false);
        return rows.Select(ToEntity).ToList();
    }

    public async Task SaveAsync(SceneEntity entity)
    {
        var empty = (EmptyEntity)entity;
        await using EditorDbContext context = _storage.CreateContext();

        EmptyRecord? existing = empty.RecordId is int id
            ? await context.Empties.FindAsync(id).ConfigureAwait(false)
            : null;
        EmptyRecord record = existing ?? new EmptyRecord();

        WriteRecord(empty, record);
        if (existing == null)
        {
            context.Empties.Add(record);
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
        empty.RecordId = record.Id;
    }

    private static EmptyEntity ToEntity(EmptyRecord record)
    {
        var empty = new EmptyEntity
        {
            Name = record.Name,
            Shape = (EmptyShape)record.Shape,
            RecordId = record.Id,
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
        record.PosX = transform.Origin.X;
        record.PosY = transform.Origin.Y;
        record.PosZ = transform.Origin.Z;
        record.RotX = rotation.X;
        record.RotY = rotation.Y;
        record.RotZ = rotation.Z;
        record.RotW = rotation.W;
    }
}
