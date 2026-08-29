using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

public sealed class SceneTerrainValueComponentRecord
{
    public int EntityId { get; set; }

    public double Width { get; set; } = 64.0;

    public double Height { get; set; } = 64.0;

    public double Value { get; set; } = 0.1;

    public SceneEntityRecord? Entity { get; set; }
}

public sealed class SceneTerrainValueChannelRecord
{
    public int EntityId { get; set; }

    public int SortOrder { get; set; }

    public string Channel { get; set; } = "";

    public SceneTerrainValueComponentRecord? Component { get; set; }
}

[Subsystem(nameof(EditorStorage))]
public sealed class TerrainValueComponentPersistence : ISceneComponentPersistence
{
    public TerrainValueComponentPersistence(EditorStorage storage)
    {
    }

    public float Priority => 0.0f;

    public string TypeId => "landscape-terrain-value";

    public void Configure(ModelBuilder model)
    {
        model.Entity<SceneTerrainValueComponentRecord>(entity =>
        {
            entity.ToTable("scene_terrain_value_components");
            entity.HasKey(record => record.EntityId);
            entity.HasOne(record => record.Entity)
                .WithOne()
                .HasForeignKey<SceneTerrainValueComponentRecord>(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<SceneTerrainValueChannelRecord>(entity =>
        {
            entity.ToTable("scene_terrain_value_channels");
            entity.HasKey(record => new { record.EntityId, record.SortOrder });
            entity.HasOne(record => record.Component)
                .WithMany()
                .HasForeignKey(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public async Task LoadAsync(EditorDbContext context, IReadOnlyDictionary<int, SceneEntity> byId, IReadOnlyList<int> ids)
    {
        List<SceneTerrainValueComponentRecord> rows = await context.Set<SceneTerrainValueComponentRecord>().AsNoTracking()
            .Where(record => ids.Contains(record.EntityId))
            .ToListAsync()
            .ConfigureAwait(false);

        Dictionary<int, List<SceneTerrainValueChannelRecord>> channels =
            await context.Set<SceneTerrainValueChannelRecord>().AsNoTracking()
                .Where(record => ids.Contains(record.EntityId))
                .OrderBy(record => record.SortOrder)
                .GroupBy(record => record.EntityId)
                .ToDictionaryAsync(group => group.Key, group => group.ToList())
                .ConfigureAwait(false);

        foreach (SceneTerrainValueComponentRecord row in rows)
        {
            if (!byId.TryGetValue(row.EntityId, out SceneEntity? entity))
            {
                continue;
            }

            var terrainValue = new TerrainValueComponent
            {
                Width = (float)row.Width,
                Height = (float)row.Height,
                Value = (float)row.Value,
            };

            if (channels.TryGetValue(row.EntityId, out List<SceneTerrainValueChannelRecord>? channelRows))
            {
                terrainValue.ReplaceChannels(channelRows.Select(entry => entry.Channel));
            }

            entity.LoadComponent(terrainValue);
        }
    }

    public void Stage(EditorDbContext context, SceneEntity entity, SceneEntityRecord entityRow)
    {
        TerrainValueComponent? terrainValue = entity.Component<TerrainValueComponent>();
        if (terrainValue == null)
        {
            if (entity.RecordId is int id)
            {
                StageDelete(context, id);
            }

            return;
        }

        var row = new SceneTerrainValueComponentRecord
        {
            Entity = entity.RecordId is null ? entityRow : null,
            EntityId = entity.RecordId ?? 0,
            Width = terrainValue.Width,
            Height = terrainValue.Height,
            Value = terrainValue.Value,
        };
        EditorComponentPersistenceHelpers.StageRow(context, row, entity.RecordId);
        StageChannels(context, terrainValue, row, entity.RecordId);
    }

    public void StageDelete(EditorDbContext context, int entityId) =>
        EditorComponentPersistenceHelpers.StageDelete<SceneTerrainValueComponentRecord>(context, entityId);

    private static void StageChannels(
        EditorDbContext context,
        TerrainValueComponent terrainValue,
        SceneTerrainValueComponentRecord row,
        int? entityId)
    {
        if (entityId is int id)
        {
            List<SceneTerrainValueChannelRecord> existing = context.Set<SceneTerrainValueChannelRecord>()
                .Where(record => record.EntityId == id)
                .OrderBy(record => record.SortOrder)
                .ToList();

            for (int i = 0; i < existing.Count; i++)
            {
                if (i >= terrainValue.Channels.Count)
                {
                    context.Set<SceneTerrainValueChannelRecord>().Remove(existing[i]);
                    continue;
                }

                existing[i].Channel = terrainValue.Channels[i];
            }

            for (int i = existing.Count; i < terrainValue.Channels.Count; i++)
            {
                context.Set<SceneTerrainValueChannelRecord>().Add(new SceneTerrainValueChannelRecord
                {
                    EntityId = id,
                    SortOrder = i,
                    Channel = terrainValue.Channels[i],
                });
            }

            return;
        }

        for (int i = 0; i < terrainValue.Channels.Count; i++)
        {
            context.Set<SceneTerrainValueChannelRecord>().Add(new SceneTerrainValueChannelRecord
            {
                Component = entityId is null ? row : null,
                EntityId = entityId ?? 0,
                SortOrder = i,
                Channel = terrainValue.Channels[i],
            });
        }
    }
}
