using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

public sealed class SceneMarkerComponentRecord
{
    public int EntityId { get; set; }

    public int Shape { get; set; }

    public SceneEntityRecord? Entity { get; set; }
}

[Subsystem(nameof(EditorStorage))]
public sealed class MarkerComponentPersistence : ISceneComponentPersistence
{
    public MarkerComponentPersistence(EditorStorage storage)
    {
    }

    public float Priority => 0.0f;

    public string TypeId => MarkerComponent.Kind;

    public void Configure(ModelBuilder model)
    {
        model.Entity<SceneMarkerComponentRecord>(entity =>
        {
            entity.ToTable("wms_scene_marker_components");
            entity.HasKey(record => record.EntityId);
            entity.HasOne(record => record.Entity)
                .WithOne()
                .HasForeignKey<SceneMarkerComponentRecord>(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public async Task LoadAsync(EditorDbContext context, IReadOnlyDictionary<int, SceneEntity> byId, IReadOnlyList<int> ids, SceneEntityScanCatalog catalog)
    {
        List<SceneMarkerComponentRecord> rows = await context.Set<SceneMarkerComponentRecord>().AsNoTracking()
            .Where(record => ids.Contains(record.EntityId))
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (SceneMarkerComponentRecord row in rows)
        {
            if (byId.TryGetValue(row.EntityId, out SceneEntity? entity))
            {
                entity.LoadComponent(new MarkerComponent { Shape = (MarkerShape)row.Shape });
            }
        }
    }

    public void Stage(EditorDbContext context, SceneEntity entity, SceneEntityRecord entityRow)
    {
        MarkerComponent? marker = entity.Component<MarkerComponent>();
        if (marker == null)
        {
            if (entity.RecordId is int id)
            {
                StageDelete(context, id);
            }

            return;
        }

        var row = new SceneMarkerComponentRecord
        {
            Entity = entity.RecordId is null ? entityRow : null,
            EntityId = entity.RecordId ?? 0,
            Shape = (int)marker.Shape,
        };
        EditorComponentPersistenceHelpers.StageRow(context, row, entity.RecordId);
    }

    public void StageDelete(EditorDbContext context, int entityId) =>
        EditorComponentPersistenceHelpers.StageDelete<SceneMarkerComponentRecord>(context, entityId);
}
