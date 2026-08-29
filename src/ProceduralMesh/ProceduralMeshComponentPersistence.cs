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

    /// <summary>Empty on a row written before formats existed; the component resolves that the same
    /// way as an unset value (defer to the bound function's default).</summary>
    public string FormatId { get; set; } = "";

    public string Materials { get; set; } = "";

    public SceneEntityRecord? Entity { get; set; }
}

[Subsystem(nameof(EditorStorage))]
public sealed class ProceduralMeshComponentPersistence : ISceneComponentPersistence
{
    private readonly EditorStorage _storage;

    public ProceduralMeshComponentPersistence(EditorStorage storage)
    {
        _storage = storage;
    }

    /// <summary>
    /// Resolved on use rather than captured in the constructor. This persister is constructed from
    /// <see cref="EditorStorage"/>'s subsystem init, which runs inside <c>new DatabaseSystem(this)</c>
    /// — and <see cref="EditorContext"/> only assigns <see cref="EditorContext.ProceduralMeshes"/>
    /// two lines later, so capturing it here would store null for the lifetime of the editor and give
    /// every loaded component a null system. <see cref="EditorStorage.Assets"/> and
    /// <see cref="EditorStorage.MeshMaterials"/> are forwarding properties for the same reason.
    /// </summary>
    private ProceduralMeshSystem ProceduralMeshes => _storage.Context.ProceduralMeshes;

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

            var mesh = new ProceduralMeshComponent(ProceduralMeshes)
            {
                FunctionId = row.FunctionId,
                Parameters = row.Parameters,
                FormatId = row.FormatId,
                Materials = row.Materials,
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
            FormatId = proceduralMesh.FormatId,
            Materials = proceduralMesh.Materials,
        };
        EditorComponentPersistenceHelpers.StageRow(context, row, entity.RecordId);
    }

    public void StageDelete(EditorDbContext context, int entityId) =>
        EditorComponentPersistenceHelpers.StageDelete<SceneProceduralMeshComponentRecord>(context, entityId);
}
