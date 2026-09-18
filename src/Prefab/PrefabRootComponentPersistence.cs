using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

public sealed class ScenePrefabRootComponentRecord
{
    public int EntityId { get; set; }

    public int PrefabId { get; set; }

    public SceneEntityRecord? Entity { get; set; }
}

[Subsystem(nameof(EditorStorage))]
public sealed class PrefabRootComponentPersistence : ISceneComponentPersistence
{
    private readonly EditorStorage _storage;

    public PrefabRootComponentPersistence(EditorStorage storage)
    {
        _storage = storage;
    }

    public float Priority => 0.0f;

    public string TypeId => PrefabRootComponent.Kind;

    public void Configure(ModelBuilder model)
    {
        model.Entity<ScenePrefabRootComponentRecord>(entity =>
        {
            entity.ToTable("wms_scene_prefab_root_components");
            entity.HasKey(record => record.EntityId);
            entity.HasOne(record => record.Entity)
                .WithOne()
                .HasForeignKey<ScenePrefabRootComponentRecord>(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public async Task LoadAsync(EditorDbContext context, IReadOnlyDictionary<int, SceneEntity> byId, IReadOnlyList<int> ids, SceneEntityScanCatalog catalog)
    {
        List<ScenePrefabRootComponentRecord> rows = await context.Set<ScenePrefabRootComponentRecord>().AsNoTracking()
            .Where(record => ids.Contains(record.EntityId))
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (ScenePrefabRootComponentRecord row in rows)
        {
            if (byId.TryGetValue(row.EntityId, out SceneEntity? entity))
            {
                entity.LoadComponent(new PrefabRootComponent { PrefabId = row.PrefabId });
            }
        }
    }

    public void Stage(EditorDbContext context, SceneEntity entity, SceneEntityRecord entityRow)
    {
        PrefabRootComponent? root = entity.Component<PrefabRootComponent>();
        if (root == null)
        {
            if (entity.RecordId is int id)
            {
                StageDelete(context, id);
            }

            return;
        }

        var row = new ScenePrefabRootComponentRecord
        {
            Entity = entity.RecordId is null ? entityRow : null,
            EntityId = entity.RecordId ?? 0,
            PrefabId = root.PrefabId,
        };
        EditorComponentPersistenceHelpers.StageRow(context, row, entity.RecordId);
    }

    public void StageDelete(EditorDbContext context, int entityId) =>
        EditorComponentPersistenceHelpers.StageDelete<ScenePrefabRootComponentRecord>(context, entityId);

    public Task DeleteForMapAsync(EditorDbContext context, DbTransaction transaction, MapId map) =>
        EditorComponentPersistenceHelpers.DeleteForMapAsync<ScenePrefabRootComponentRecord>(_storage, context, transaction, map);
}
