using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

public sealed class SceneProceduralComponentRecord
{
    public int EntityId { get; set; }

    /// <summary>The referenced <see cref="ProceduralModel"/>'s row id. Null for a placement created
    /// without ever picking a model.</summary>
    public int? ModelId { get; set; }

    public SceneEntityRecord? Entity { get; set; }
}

[Subsystem(nameof(EditorStorage))]
public sealed class ProceduralComponentPersistence : ISceneComponentPersistence
{
    private readonly EditorStorage _storage;

    public ProceduralComponentPersistence(EditorStorage storage)
    {
        _storage = storage;
    }

    /// <summary>
    /// Resolved on use rather than captured in the constructor. This persister is constructed from
    /// <see cref="EditorStorage"/>'s subsystem init, which runs inside <c>new DatabaseSystem(this)</c>
    /// — and <see cref="EditorContext"/> only assigns <see cref="EditorContext.Procedural"/>
    /// two lines later, so capturing it here would store null for the lifetime of the editor and give
    /// every loaded component a null system. <see cref="EditorStorage.Assets"/> and
    /// <see cref="EditorStorage.MeshMaterials"/> are forwarding properties for the same reason.
    /// </summary>
    private ProceduralSystem Procedural => _storage.Context.Procedural;

    public float Priority => 0.0f;

    public string TypeId => ProceduralComponent.Kind;

    public void Configure(ModelBuilder model)
    {
        model.Entity<SceneProceduralComponentRecord>(entity =>
        {
            entity.ToTable("scene_procedural_mesh_components");
            entity.HasKey(record => record.EntityId);
            entity.HasOne(record => record.Entity)
                .WithOne()
                .HasForeignKey<SceneProceduralComponentRecord>(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public async Task LoadAsync(EditorDbContext context, IReadOnlyDictionary<int, SceneEntity> byId, IReadOnlyList<int> ids)
    {
        List<SceneProceduralComponentRecord> rows = await context.Set<SceneProceduralComponentRecord>().AsNoTracking()
            .Where(record => ids.Contains(record.EntityId))
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (SceneProceduralComponentRecord row in rows)
        {
            if (!byId.TryGetValue(row.EntityId, out SceneEntity? entity))
            {
                continue;
            }

            var mesh = new ProceduralComponent(Procedural) { ModelId = row.ModelId };
            entity.LoadComponent(mesh);
        }
    }

    public void Stage(EditorDbContext context, SceneEntity entity, SceneEntityRecord entityRow)
    {
        ProceduralComponent? proceduralMesh = entity.Component<ProceduralComponent>();
        if (proceduralMesh == null)
        {
            if (entity.RecordId is int id)
            {
                StageDelete(context, id);
            }

            return;
        }

        var row = new SceneProceduralComponentRecord
        {
            Entity = entity.RecordId is null ? entityRow : null,
            EntityId = entity.RecordId ?? 0,
            ModelId = proceduralMesh.ModelId,
        };
        EditorComponentPersistenceHelpers.StageRow(context, row, entity.RecordId);
    }

    public void StageDelete(EditorDbContext context, int entityId) =>
        EditorComponentPersistenceHelpers.StageDelete<SceneProceduralComponentRecord>(context, entityId);
}
