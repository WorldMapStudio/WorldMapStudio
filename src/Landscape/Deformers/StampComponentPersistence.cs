using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

public sealed class SceneStampComponentRecord
{
    public int EntityId { get; set; }

    public double Radius { get; set; } = 24.0;

    public double Falloff { get; set; } = 0.5;

    public double Strength { get; set; } = 1.0;

    public string Channel { get; set; } = "";

    public double ColorR { get; set; } = 1.0;

    public double ColorG { get; set; } = 1.0;

    public double ColorB { get; set; } = 1.0;

    public double ColorA { get; set; } = 1.0;

    public SceneEntityRecord? Entity { get; set; }
}

[Subsystem(nameof(EditorStorage))]
public sealed class StampComponentPersistence : ISceneComponentPersistence
{
    public StampComponentPersistence(EditorStorage storage)
    {
    }

    public float Priority => 0.0f;

    public string TypeId => StampComponent.Kind;

    public void Configure(ModelBuilder model)
    {
        model.Entity<SceneStampComponentRecord>(entity =>
        {
            entity.ToTable("wms_scene_stamp_components");
            entity.HasKey(record => record.EntityId);
            entity.HasOne(record => record.Entity)
                .WithOne()
                .HasForeignKey<SceneStampComponentRecord>(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public async Task LoadAsync(EditorDbContext context, IReadOnlyDictionary<int, SceneEntity> byId, IReadOnlyList<int> ids)
    {
        List<SceneStampComponentRecord> rows = await context.Set<SceneStampComponentRecord>().AsNoTracking()
            .Where(record => ids.Contains(record.EntityId))
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (SceneStampComponentRecord row in rows)
        {
            if (byId.TryGetValue(row.EntityId, out SceneEntity? entity))
            {
                entity.LoadComponent(new StampComponent
                {
                    Radius = (float)row.Radius,
                    Falloff = (float)row.Falloff,
                    Strength = (float)row.Strength,
                    Channel = row.Channel,
                    ColorValue = new Godot.Color((float)row.ColorR, (float)row.ColorG, (float)row.ColorB, (float)row.ColorA),
                });
            }
        }
    }

    public void Stage(EditorDbContext context, SceneEntity entity, SceneEntityRecord entityRow)
    {
        StampComponent? stamp = entity.Component<StampComponent>();
        if (stamp == null)
        {
            if (entity.RecordId is int id)
            {
                StageDelete(context, id);
            }

            return;
        }

        var row = new SceneStampComponentRecord
        {
            Entity = entity.RecordId is null ? entityRow : null,
            EntityId = entity.RecordId ?? 0,
            Radius = stamp.Radius,
            Falloff = stamp.Falloff,
            Strength = stamp.Strength,
            Channel = stamp.Channel,
            ColorR = stamp.ColorValue.R,
            ColorG = stamp.ColorValue.G,
            ColorB = stamp.ColorValue.B,
            ColorA = stamp.ColorValue.A,
        };
        EditorComponentPersistenceHelpers.StageRow(context, row, entity.RecordId);
    }

    public void StageDelete(EditorDbContext context, int entityId) =>
        EditorComponentPersistenceHelpers.StageDelete<SceneStampComponentRecord>(context, entityId);
}
