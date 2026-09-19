using System.Collections.Generic;
using System.Data.Common;
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

    public double ColorR { get; set; } = 1.0;

    public double ColorG { get; set; } = 1.0;

    public double ColorB { get; set; } = 1.0;

    public double ColorA { get; set; } = 1.0;

    public EntityRecord? Entity { get; set; }
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
    private readonly EditorStorage _storage;

    public TerrainValueComponentPersistence(EditorStorage storage)
    {
        _storage = storage;
    }

    public string TypeId => TerrainValueComponent.Kind;

    public void Configure(ModelBuilder model)
    {
        model.Entity<SceneTerrainValueComponentRecord>(entity =>
        {
            entity.ToTable("wms_scene_terrain_value_components");
            entity.HasKey(record => record.EntityId);
            entity.HasOne(record => record.Entity)
                .WithOne()
                .HasForeignKey<SceneTerrainValueComponentRecord>(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<SceneTerrainValueChannelRecord>(entity =>
        {
            entity.ToTable("wms_scene_terrain_value_channels");
            entity.HasKey(record => new { record.EntityId, record.SortOrder });
            entity.HasOne(record => record.Component)
                .WithMany()
                .HasForeignKey(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public async Task LoadAsync(EditorDbContext context, IReadOnlyDictionary<int, SceneEntity> byId, IReadOnlyList<int> ids, SceneEntityScanCatalog catalog)
    {
        List<SceneTerrainValueComponentRecord> rows = await context.Set<SceneTerrainValueComponentRecord>().AsNoTracking()
            .Where(record => ids.Contains(record.EntityId))
            .ToListAsync()
            .ConfigureAwait(false);

        // Keyed off the rows this component actually has, not the scan's whole id set: the child
        // table is keyed (EntityId, SortOrder), and a several-thousand-element IN against a composite
        // key's leading column costs hundreds of milliseconds regardless of how few rows come back.
        int[] owners = rows.Select(row => row.EntityId).ToArray();
        Dictionary<int, List<SceneTerrainValueChannelRecord>> channels = owners.Length == 0
            ? []
            : await context.Set<SceneTerrainValueChannelRecord>().AsNoTracking()
                .Where(record => owners.Contains(record.EntityId))
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
                ColorValue = new Godot.Color((float)row.ColorR, (float)row.ColorG, (float)row.ColorB, (float)row.ColorA),
            };

            if (channels.TryGetValue(row.EntityId, out List<SceneTerrainValueChannelRecord>? channelRows))
            {
                terrainValue.ReplaceChannels(channelRows.Select(entry => entry.Channel));
            }

            entity.LoadComponent(terrainValue);
        }
    }

    public void Stage(EditorDbContext context, SceneEntity entity, EntityRecord entityRow)
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
            ColorR = terrainValue.ColorValue.R,
            ColorG = terrainValue.ColorValue.G,
            ColorB = terrainValue.ColorValue.B,
            ColorA = terrainValue.ColorValue.A,
        };
        EditorComponentPersistenceHelpers.StageRow(context, row, entity.RecordId);
        StageChannels(context, terrainValue, row, entity.RecordId);
    }

    public void StageDelete(EditorDbContext context, int entityId) =>
        EditorComponentPersistenceHelpers.StageDelete<SceneTerrainValueComponentRecord>(context, entityId);

    public async Task DeleteForMapAsync(EditorDbContext context, DbTransaction transaction, MapId map)
    {
        await _storage.DeleteForMapEntitiesAsync<SceneTerrainValueChannelRecord>(context, transaction, nameof(SceneTerrainValueChannelRecord.EntityId), map)
            .ConfigureAwait(false);
        await _storage.DeleteForMapEntitiesAsync<SceneTerrainValueComponentRecord>(context, transaction, nameof(SceneTerrainValueComponentRecord.EntityId), map)
            .ConfigureAwait(false);
    }

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
