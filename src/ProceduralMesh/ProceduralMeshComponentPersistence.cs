using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

public sealed class SceneProceduralMeshComponentRecord
{
    public int EntityId { get; set; }

    public string FunctionId { get; set; } = "";

    public string Parameters { get; set; } = "";

    public string NetworkJson { get; set; } = "";

    public SceneEntityRecord? Entity { get; set; }
}

[Subsystem(nameof(EditorStorage))]
public sealed class ProceduralMeshComponentPersistence : ISceneComponentPersistence
{
    private readonly ProceduralMeshSystem _proceduralMeshes;

    public ProceduralMeshComponentPersistence(EditorStorage storage)
    {
        _proceduralMeshes = storage.Context.ProceduralMeshes;
    }

    public float Priority => 0.0f;

    public string TypeId => "procedural-mesh";

    public void Configure(ModelBuilder model)
    {
        model.Entity<SceneProceduralMeshComponentRecord>(entity =>
        {
            entity.ToTable("scene_procedural_mesh_components");
            entity.HasKey(record => record.EntityId);
            entity.HasOne(record => record.Entity)
                .WithOne()
                .HasForeignKey<SceneProceduralMeshComponentRecord>(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public async Task LoadAsync(EditorDbContext context, IReadOnlyDictionary<int, SceneEntity> byId, IReadOnlyList<int> ids)
    {
        List<SceneProceduralMeshComponentRecord> rows = await context.Set<SceneProceduralMeshComponentRecord>().AsNoTracking()
            .Where(record => ids.Contains(record.EntityId))
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (SceneProceduralMeshComponentRecord row in rows)
        {
            if (!byId.TryGetValue(row.EntityId, out SceneEntity? entity))
            {
                continue;
            }

            var mesh = new ProceduralMeshComponent(_proceduralMeshes)
            {
                FunctionId = row.FunctionId,
                Parameters = row.Parameters,
            };
            mesh.ReplaceNetwork(VertexNetwork.Parse(row.NetworkJson));
            entity.LoadComponent(mesh);
        }
    }

    public void Stage(EditorDbContext context, SceneEntity entity, SceneEntityRecord entityRow)
    {
        ProceduralMeshComponent? proceduralMesh = entity.Component<ProceduralMeshComponent>();
        if (proceduralMesh == null)
        {
            if (entity.RecordId is int id)
            {
                StageDelete(context, id);
            }

            return;
        }

        var row = new SceneProceduralMeshComponentRecord
        {
            Entity = entity.RecordId is null ? entityRow : null,
            EntityId = entity.RecordId ?? 0,
            FunctionId = proceduralMesh.FunctionId,
            Parameters = proceduralMesh.Parameters,
            NetworkJson = proceduralMesh.Network.Serialize(),
        };
        EditorComponentPersistenceHelpers.StageRow(context, row, entity.RecordId);
    }

    public void StageDelete(EditorDbContext context, int entityId) =>
        EditorComponentPersistenceHelpers.StageDelete<SceneProceduralMeshComponentRecord>(context, entityId);
}
