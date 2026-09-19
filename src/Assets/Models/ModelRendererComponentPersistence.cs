using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

public sealed class SceneModelRendererComponentRecord
{
    public int EntityId { get; set; }

    public string ModelPath { get; set; } = "";

    /// <summary>Opaque format-specific state owned by a <see cref="ModelRendererComponent"/> partial
    /// extension; stored and returned verbatim.</summary>
    public string? FormatState { get; set; }

    public MapEntityRecord? Entity { get; set; }
}

[Subsystem(nameof(EditorStorage))]
public sealed class ModelRendererComponentPersistence : ISceneComponentPersistence
{
    private readonly EditorStorage _storage;
    private readonly AssetSystem _assets;
    private readonly MeshMaterialSystem _materials;

    public ModelRendererComponentPersistence(EditorStorage storage)
    {
        _storage = storage;
        _assets = storage.Assets;
        _materials = storage.MeshMaterials;
    }

    public string TypeId => ModelRendererComponent.Kind;

    public void Configure(ModelBuilder model)
    {
        model.Entity<SceneModelRendererComponentRecord>(entity =>
        {
            entity.ToTable("wms_scene_model_renderer_components");
            entity.HasKey(record => record.EntityId);
            entity.HasOne(record => record.Entity)
                .WithOne()
                .HasForeignKey<SceneModelRendererComponentRecord>(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public async Task LoadAsync(EditorDbContext context, IReadOnlyDictionary<int, SceneEntity> byId, IReadOnlyList<int> ids, SceneEntityScanCatalog catalog)
    {
        List<SceneModelRendererComponentRecord> rows = await context.Set<SceneModelRendererComponentRecord>().AsNoTracking()
            .Where(record => ids.Contains(record.EntityId))
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (SceneModelRendererComponentRecord row in rows)
        {
            if (byId.TryGetValue(row.EntityId, out SceneEntity? entity))
            {
                entity.LoadComponent(new ModelRendererComponent(_assets, _materials)
                {
                    ModelPath = row.ModelPath,
                    FormatState = row.FormatState,
                });
            }
        }
    }

    public void Stage(EditorDbContext context, SceneEntity entity, MapEntityRecord entityRow)
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
            FormatState = model.FormatState,
        };
        EditorComponentPersistenceHelpers.StageRow(context, row, entity.RecordId);
    }

    public void StageDelete(EditorDbContext context, int entityId) =>
        EditorComponentPersistenceHelpers.StageDelete<SceneModelRendererComponentRecord>(context, entityId);

    public Task DeleteForMapAsync(EditorDbContext context, DbTransaction transaction, MapId map) =>
        EditorComponentPersistenceHelpers.DeleteForMapAsync<SceneModelRendererComponentRecord>(_storage, context, transaction, map);
}
