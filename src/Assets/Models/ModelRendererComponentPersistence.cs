using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

public sealed class SceneModelRendererComponentRecord
{
    public int EntityId { get; set; }

    public string ModelPath { get; set; } = "";

    public SceneEntityRecord? Entity { get; set; }
}

[Subsystem(nameof(EditorStorage))]
public sealed class ModelRendererComponentPersistence : ISceneComponentPersistence
{
    private readonly AssetSystem _assets;
    private readonly MeshMaterialSystem _materials;

    public ModelRendererComponentPersistence(EditorStorage storage)
    {
        _assets = storage.Assets;
        _materials = storage.MeshMaterials;
    }

    public float Priority => 0.0f;

    public string TypeId => "model-renderer";

    public void Configure(ModelBuilder model)
    {
        model.Entity<SceneModelRendererComponentRecord>(entity =>
        {
            entity.ToTable("scene_model_renderer_components");
            entity.HasKey(record => record.EntityId);
            entity.HasOne(record => record.Entity)
                .WithOne()
                .HasForeignKey<SceneModelRendererComponentRecord>(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public async Task LoadAsync(EditorDbContext context, IReadOnlyDictionary<int, SceneEntity> byId, IReadOnlyList<int> ids)
    {
        List<SceneModelRendererComponentRecord> rows = await context.Set<SceneModelRendererComponentRecord>().AsNoTracking()
            .Where(record => ids.Contains(record.EntityId))
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (SceneModelRendererComponentRecord row in rows)
        {
            if (byId.TryGetValue(row.EntityId, out SceneEntity? entity))
            {
                entity.LoadComponent(new ModelRendererComponent(_assets, _materials) { ModelPath = row.ModelPath });
            }
        }
    }

    public void Stage(EditorDbContext context, SceneEntity entity, SceneEntityRecord entityRow)
    {
        ModelRendererComponent? model = entity.Component<ModelRendererComponent>();
        if (model == null)
        {
            if (entity.RecordId is int id)
            {
                StageDelete(context, id);
            }

            return;
        }

        var row = new SceneModelRendererComponentRecord
        {
            Entity = entity.RecordId is null ? entityRow : null,
            EntityId = entity.RecordId ?? 0,
            ModelPath = model.ModelPath,
        };
        EditorComponentPersistenceHelpers.StageRow(context, row, entity.RecordId);
    }

    public void StageDelete(EditorDbContext context, int entityId) =>
        EditorComponentPersistenceHelpers.StageDelete<SceneModelRendererComponentRecord>(context, entityId);
}
