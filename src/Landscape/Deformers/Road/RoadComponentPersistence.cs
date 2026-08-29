using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

public sealed class SceneRoadComponentRecord
{
    public int EntityId { get; set; }

    public double CentreWidth { get; set; } = 4.0;

    public double ShoulderWidth { get; set; } = 3.0;

    public double Falloff { get; set; } = 0.35;

    public string CentreChannel { get; set; } = "";

    public string ShoulderChannel { get; set; } = "";

    public string NetworkJson { get; set; } = "";

    public SceneEntityRecord? Entity { get; set; }
}

[Subsystem(nameof(EditorStorage))]
public sealed class RoadComponentPersistence : ISceneComponentPersistence
{
    public RoadComponentPersistence(EditorStorage storage)
    {
    }

    public float Priority => 0.0f;

    public string TypeId => "landscape-road";

    public void Configure(ModelBuilder model)
    {
        model.Entity<SceneRoadComponentRecord>(entity =>
        {
            entity.ToTable("scene_road_components");
            entity.HasKey(record => record.EntityId);
            entity.HasOne(record => record.Entity)
                .WithOne()
                .HasForeignKey<SceneRoadComponentRecord>(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public async Task LoadAsync(EditorDbContext context, IReadOnlyDictionary<int, SceneEntity> byId, IReadOnlyList<int> ids)
    {
        List<SceneRoadComponentRecord> rows = await context.Set<SceneRoadComponentRecord>().AsNoTracking()
            .Where(record => ids.Contains(record.EntityId))
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (SceneRoadComponentRecord row in rows)
        {
            if (!byId.TryGetValue(row.EntityId, out SceneEntity? entity))
            {
                continue;
            }

            var road = new RoadComponent
            {
                CentreWidth = (float)row.CentreWidth,
                ShoulderWidth = (float)row.ShoulderWidth,
                Falloff = (float)row.Falloff,
                CentreChannel = row.CentreChannel,
                ShoulderChannel = row.ShoulderChannel,
            };
            road.ReplaceNetwork(VertexNetwork.Parse(row.NetworkJson));
            entity.LoadComponent(road);
        }
    }

    public void Stage(EditorDbContext context, SceneEntity entity, SceneEntityRecord entityRow)
    {
        RoadComponent? road = entity.Component<RoadComponent>();
        if (road == null)
        {
            if (entity.RecordId is int id)
            {
                StageDelete(context, id);
            }

            return;
        }

        var row = new SceneRoadComponentRecord
        {
            Entity = entity.RecordId is null ? entityRow : null,
            EntityId = entity.RecordId ?? 0,
            CentreWidth = road.CentreWidth,
            ShoulderWidth = road.ShoulderWidth,
            Falloff = road.Falloff,
            CentreChannel = road.CentreChannel,
            ShoulderChannel = road.ShoulderChannel,
            NetworkJson = road.Network.Serialize(),
        };
        EditorComponentPersistenceHelpers.StageRow(context, row, entity.RecordId);
    }

    public void StageDelete(EditorDbContext context, int entityId) =>
        EditorComponentPersistenceHelpers.StageDelete<SceneRoadComponentRecord>(context, entityId);
}
