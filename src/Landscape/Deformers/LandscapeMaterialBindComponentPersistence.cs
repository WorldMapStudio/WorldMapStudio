using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

public sealed class SceneLandscapeMaterialBindComponentRecord
{
    public int EntityId { get; set; }

    public int Priority { get; set; }

    public SceneEntityRecord? Entity { get; set; }
}

public sealed class SceneLandscapeMaterialBindEntryRecord
{
    public int EntityId { get; set; }

    public int SortOrder { get; set; }

    public int? LayerId { get; set; }

    public int? MaterialId { get; set; }

    public SceneLandscapeMaterialBindComponentRecord? Component { get; set; }
}

[Subsystem(nameof(EditorStorage))]
public sealed class LandscapeMaterialBindComponentPersistence : ISceneComponentPersistence
{
    public LandscapeMaterialBindComponentPersistence(EditorStorage storage)
    {
    }

    public float Priority => 0.0f;

    public string TypeId => LandscapeMaterialBindComponent.Kind;

    public void Configure(ModelBuilder model)
    {
        model.Entity<SceneLandscapeMaterialBindComponentRecord>(entity =>
        {
            entity.ToTable("scene_landscape_material_bind_components");
            entity.HasKey(record => record.EntityId);
            entity.HasOne(record => record.Entity)
                .WithOne()
                .HasForeignKey<SceneLandscapeMaterialBindComponentRecord>(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<SceneLandscapeMaterialBindEntryRecord>(entity =>
        {
            entity.ToTable("scene_landscape_material_bind_entries");
            entity.HasKey(record => new { record.EntityId, record.SortOrder });
            entity.HasOne(record => record.Component)
                .WithMany()
                .HasForeignKey(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public async Task LoadAsync(EditorDbContext context, IReadOnlyDictionary<int, SceneEntity> byId, IReadOnlyList<int> ids)
    {
        List<SceneLandscapeMaterialBindComponentRecord> rows = await context.Set<SceneLandscapeMaterialBindComponentRecord>().AsNoTracking()
            .Where(record => ids.Contains(record.EntityId))
            .ToListAsync()
            .ConfigureAwait(false);

        Dictionary<int, List<SceneLandscapeMaterialBindEntryRecord>> entries =
            await context.Set<SceneLandscapeMaterialBindEntryRecord>().AsNoTracking()
                .Where(record => ids.Contains(record.EntityId))
                .OrderBy(record => record.SortOrder)
                .GroupBy(record => record.EntityId)
                .ToDictionaryAsync(group => group.Key, group => group.ToList())
                .ConfigureAwait(false);

        foreach (SceneLandscapeMaterialBindComponentRecord row in rows)
        {
            if (!byId.TryGetValue(row.EntityId, out SceneEntity? entity))
            {
                continue;
            }

            var bind = new LandscapeMaterialBindComponent { Priority = row.Priority };
            if (entries.TryGetValue(row.EntityId, out List<SceneLandscapeMaterialBindEntryRecord>? bindingRows))
            {
                bind.ReplaceBindings(bindingRows.Select(entry => new LandscapeMaterialBinding(entry.LayerId, entry.MaterialId)));
            }

            entity.LoadComponent(bind);
        }
    }

    public void Stage(EditorDbContext context, SceneEntity entity, SceneEntityRecord entityRow)
    {
        LandscapeMaterialBindComponent? bind = entity.Component<LandscapeMaterialBindComponent>();
        if (bind == null)
        {
            if (entity.RecordId is int id)
            {
                StageDelete(context, id);
            }

            return;
        }

        var row = new SceneLandscapeMaterialBindComponentRecord
        {
            Entity = entity.RecordId is null ? entityRow : null,
            EntityId = entity.RecordId ?? 0,
            Priority = bind.Priority,
        };
        EditorComponentPersistenceHelpers.StageRow(context, row, entity.RecordId);
        StageEntries(context, bind, row, entity.RecordId);
    }

    public void StageDelete(EditorDbContext context, int entityId) =>
        EditorComponentPersistenceHelpers.StageDelete<SceneLandscapeMaterialBindComponentRecord>(context, entityId);

    private static void StageEntries(
        EditorDbContext context,
        LandscapeMaterialBindComponent bind,
        SceneLandscapeMaterialBindComponentRecord row,
        int? entityId)
    {
        if (entityId is int id)
        {
            List<SceneLandscapeMaterialBindEntryRecord> existing = context.Set<SceneLandscapeMaterialBindEntryRecord>()
                .Where(record => record.EntityId == id)
                .OrderBy(record => record.SortOrder)
                .ToList();

            for (int i = 0; i < existing.Count; i++)
            {
                if (i >= bind.Bindings.Count)
                {
                    context.Set<SceneLandscapeMaterialBindEntryRecord>().Remove(existing[i]);
                    continue;
                }

                LandscapeMaterialBinding binding = bind.Bindings[i];
                existing[i].LayerId = binding.LayerId;
                existing[i].MaterialId = binding.MaterialId;
            }

            for (int i = existing.Count; i < bind.Bindings.Count; i++)
            {
                LandscapeMaterialBinding binding = bind.Bindings[i];
                context.Set<SceneLandscapeMaterialBindEntryRecord>().Add(new SceneLandscapeMaterialBindEntryRecord
                {
                    EntityId = id,
                    SortOrder = i,
                    LayerId = binding.LayerId,
                    MaterialId = binding.MaterialId,
                });
            }

            return;
        }

        for (int i = 0; i < bind.Bindings.Count; i++)
        {
            LandscapeMaterialBinding binding = bind.Bindings[i];
            context.Set<SceneLandscapeMaterialBindEntryRecord>().Add(new SceneLandscapeMaterialBindEntryRecord
            {
                Component = entityId is null ? row : null,
                EntityId = entityId ?? 0,
                SortOrder = i,
                LayerId = binding.LayerId,
                MaterialId = binding.MaterialId,
            });
        }
    }
}
