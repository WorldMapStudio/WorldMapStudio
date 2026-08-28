using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

public sealed class SceneDrawingTargetComponentRecord
{
    public int EntityId { get; set; }

    public int Width { get; set; } = 256;

    public int Height { get; set; } = 256;

    public double WorldSizeX { get; set; } = 64.0;

    public double WorldSizeZ { get; set; } = 64.0;

    public double Strength { get; set; } = 1.0;

    public string Channel { get; set; } = "";

    public byte[] Pixels { get; set; } = [];

    public SceneEntityRecord? Entity { get; set; }
}

[Subsystem(nameof(EditorStorage))]
public sealed class DrawingTargetComponentPersistence : ISceneComponentPersistence
{
    public DrawingTargetComponentPersistence(EditorStorage storage)
    {
    }

    public float Priority => 0.0f;

    public string TypeId => "drawing-target";

    public void Configure(ModelBuilder model)
    {
        model.Entity<SceneDrawingTargetComponentRecord>(entity =>
        {
            entity.ToTable("scene_drawing_target_components");
            entity.HasKey(record => record.EntityId);
            entity.HasOne(record => record.Entity)
                .WithOne()
                .HasForeignKey<SceneDrawingTargetComponentRecord>(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public async Task LoadAsync(EditorDbContext context, IReadOnlyDictionary<int, SceneEntity> byId, IReadOnlyList<int> ids)
    {
        List<SceneDrawingTargetComponentRecord> rows = await context.Set<SceneDrawingTargetComponentRecord>().AsNoTracking()
            .Where(record => ids.Contains(record.EntityId))
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (SceneDrawingTargetComponentRecord row in rows)
        {
            if (!byId.TryGetValue(row.EntityId, out SceneEntity? entity))
            {
                continue;
            }

            var target = new DrawingTargetComponent
            {
                WorldSizeX = (float)row.WorldSizeX,
                WorldSizeZ = (float)row.WorldSizeZ,
                Strength = (float)row.Strength,
                Channel = row.Channel,
            };
            target.LoadPixels(row.Width, row.Height, row.Pixels);
            entity.LoadComponent(target);
        }
    }

    public void Stage(EditorDbContext context, SceneEntity entity, SceneEntityRecord entityRow)
    {
        DrawingTargetComponent? target = entity.Component<DrawingTargetComponent>();
        if (target == null)
        {
            if (entity.RecordId is int id)
            {
                StageDelete(context, id);
            }

            return;
        }

        var row = new SceneDrawingTargetComponentRecord
        {
            Entity = entity.RecordId is null ? entityRow : null,
            EntityId = entity.RecordId ?? 0,
            Width = target.Width,
            Height = target.Height,
            WorldSizeX = target.WorldSizeX,
            WorldSizeZ = target.WorldSizeZ,
            Strength = target.Strength,
            Channel = target.Channel,
            Pixels = target.CopyPixels(),
        };
        EditorComponentPersistenceHelpers.StageRow(context, row, entity.RecordId);
    }

    public void StageDelete(EditorDbContext context, int entityId) =>
        EditorComponentPersistenceHelpers.StageDelete<SceneDrawingTargetComponentRecord>(context, entityId);
}
