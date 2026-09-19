using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

public sealed class ScenePrefabTemplateComponentRecord
{
    public int EntityId { get; set; }

    public int PrefabId { get; set; }

    public MapEntityRecord? Entity { get; set; }
}

[Subsystem(nameof(EditorStorage))]
public sealed class PrefabTemplateComponentPersistence : ISceneComponentPersistence
{
    private readonly EditorStorage _storage;

    public PrefabTemplateComponentPersistence(EditorStorage storage)
    {
        _storage = storage;
    }

    public string TypeId => PrefabTemplateComponent.Kind;

    public void Configure(ModelBuilder model)
    {
        model.Entity<ScenePrefabTemplateComponentRecord>(entity =>
        {
            entity.ToTable("wms_scene_prefab_template_components");
            entity.HasKey(record => record.EntityId);
            entity.HasOne(record => record.Entity)
                .WithOne()
                .HasForeignKey<ScenePrefabTemplateComponentRecord>(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public async Task LoadAsync(EditorDbContext context, IReadOnlyDictionary<int, SceneEntity> byId, IReadOnlyList<int> ids, SceneEntityScanCatalog catalog)
    {
        List<ScenePrefabTemplateComponentRecord> rows = await context.Set<ScenePrefabTemplateComponentRecord>().AsNoTracking()
            .Where(record => ids.Contains(record.EntityId))
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (ScenePrefabTemplateComponentRecord row in rows)
        {
            if (byId.TryGetValue(row.EntityId, out SceneEntity? entity))
            {
                entity.LoadComponent(new PrefabTemplateComponent { PrefabId = row.PrefabId });
            }
        }
    }

    public void Stage(EditorDbContext context, SceneEntity entity, MapEntityRecord entityRow)
    {
        PrefabTemplateComponent? template = entity.Component<PrefabTemplateComponent>();
        if (template == null)
        {
            if (entity.RecordId is int id)
            {
                StageDelete(context, id);
            }

            return;
        }

        var row = new ScenePrefabTemplateComponentRecord
        {
            Entity = entity.RecordId is null ? entityRow : null,
            EntityId = entity.RecordId ?? 0,
            PrefabId = template.PrefabId,
        };
        EditorComponentPersistenceHelpers.StageRow(context, row, entity.RecordId);
    }

    public void StageDelete(EditorDbContext context, int entityId) =>
        EditorComponentPersistenceHelpers.StageDelete<ScenePrefabTemplateComponentRecord>(context, entityId);

    public Task DeleteForMapAsync(EditorDbContext context, DbTransaction transaction, MapId map) =>
        EditorComponentPersistenceHelpers.DeleteForMapAsync<ScenePrefabTemplateComponentRecord>(_storage, context, transaction, map);
}
