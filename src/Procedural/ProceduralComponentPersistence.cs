using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

public sealed class SceneProceduralComponentRecord
{
    public int EntityId { get; set; }

    /// <summary>The referenced <see cref="ProceduralModel"/>'s row id. Null for a placement created
    /// without ever picking a model.</summary>
    public int? ModelId { get; set; }

    public EntityRecord? Entity { get; set; }
}

[Subsystem(nameof(EditorStorage))]
public sealed class ProceduralComponentPersistence : ISceneComponentPersistence, IResourceReferencingPersistence
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

    private ProceduralModelFactory ModelFactory => _storage.ProceduralModelFactory;

    public string TypeId => ProceduralComponent.Kind;

    public void Configure(ModelBuilder model)
    {
        model.Entity<SceneProceduralComponentRecord>(entity =>
        {
            entity.ToTable("wms_scene_procedural_mesh_components");
            entity.HasKey(record => record.EntityId);
            entity.HasOne(record => record.Entity)
                .WithOne()
                .HasForeignKey<SceneProceduralComponentRecord>(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public async Task LoadAsync(EditorDbContext context, IReadOnlyDictionary<int, SceneEntity> byId, IReadOnlyList<int> ids, SceneEntityScanCatalog catalog)
    {
        List<SceneProceduralComponentRecord> rows = await context.Set<SceneProceduralComponentRecord>().AsNoTracking()
            .Where(record => ids.Contains(record.EntityId))
            .ToListAsync()
            .ConfigureAwait(false);

        var wantedIds = new HashSet<int>();
        foreach (SceneProceduralComponentRecord row in rows)
        {
            if (row.ModelId is int id)
            {
                wantedIds.Add(id);
            }
        }

        // A publishing scan lets the registry own the instance a live placement resolves to — re-reading
        // an already-resident model's 145 KiB network here would pay its parse cost again for nothing.
        if (catalog.Publishing)
        {
            wantedIds.ExceptWith(Procedural.LoadedModelIds);
        }

        // One instance per id shared by every component in this scan that names it — resolving per
        // component would silently fork a shared model into one copy per placement.
        var resolved = new Dictionary<int, ProceduralModel>();
        if (wantedIds.Count > 0)
        {
            foreach (CatalogEntity entity in await ModelFactory.LoadByIdAsync(context, wantedIds).ConfigureAwait(false))
            {
                if (entity is ProceduralModel model && model.RecordId is int id)
                {
                    resolved[id] = model;
                    catalog.Add(model);
                }
            }
        }

        foreach (SceneProceduralComponentRecord row in rows)
        {
            if (!byId.TryGetValue(row.EntityId, out SceneEntity? entity))
            {
                continue;
            }

            var mesh = new ProceduralComponent(Procedural) { ModelId = row.ModelId };
            if (row.ModelId is int modelId && resolved.TryGetValue(modelId, out ProceduralModel? model))
            {
                mesh.AttachModel(model);
            }

            entity.LoadComponent(mesh);
        }
    }

    public void Stage(EditorDbContext context, SceneEntity entity, EntityRecord entityRow)
    {
        ProceduralComponent? proceduralMesh = entity.Attached<ProceduralComponent>();
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

    public Task DeleteForMapAsync(EditorDbContext context, DbTransaction transaction, MapId map) =>
        EditorComponentPersistenceHelpers.DeleteForMapAsync<SceneProceduralComponentRecord>(_storage, context, transaction, map);

    public Type ReferencedResourceType => typeof(ProceduralModel);

    public async Task<IReadOnlyList<(int EntityId, MapId Map, Aabb Bounds)>> ReferencingBoundsAsync(
        EditorDbContext context,
        int resourceRecordId)
    {
        List<MapEntityRecord> rows = await context.Set<SceneProceduralComponentRecord>().AsNoTracking()
            .Where(record => record.ModelId == resourceRecordId)
            .Join(
                context.MapEntities.AsNoTracking(),
                record => record.EntityId,
                entity => entity.Id,
                (record, entity) => entity)
            .ToListAsync()
            .ConfigureAwait(false);

        return rows.ConvertAll(EditorComponentPersistenceHelpers.ToMapBounds);
    }

    public async Task<IReadOnlyList<(int ResourceId, MapId Map)>> ReferencesAsync(EditorDbContext context)
    {
        List<(int, int)> rows = await context.Set<SceneProceduralComponentRecord>().AsNoTracking()
            .Where(record => record.ModelId != null)
            .Join(
                context.MapEntities.AsNoTracking(),
                record => record.EntityId,
                entity => entity.Id,
                (record, entity) => new ValueTuple<int, int>(record.ModelId!.Value, entity.MapId))
            .Distinct()
            .ToListAsync()
            .ConfigureAwait(false);

        return rows.ConvertAll(row => (row.Item1, new MapId(row.Item2)));
    }
}
