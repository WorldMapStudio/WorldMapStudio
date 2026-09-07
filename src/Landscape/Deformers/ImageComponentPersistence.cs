using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

public sealed class SceneImageComponentRecord
{
    public int EntityId { get; set; }

    /// <summary>The referenced <see cref="PaintImage"/>'s row id. Null for a placement created without
    /// ever picking an image.</summary>
    public int? ImageId { get; set; }

    /// <summary>The referenced <see cref="ImageDisplayLayer"/>'s row id. Null for a placement with no
    /// viewport preview.</summary>
    public int? DisplayLayerId { get; set; }

    public double WorldSizeX { get; set; } = 64.0;

    public double WorldSizeZ { get; set; } = 64.0;

    public double Strength { get; set; } = 1.0;

    public string Channel { get; set; } = "";

    public SceneEntityRecord? Entity { get; set; }
}

[Subsystem(nameof(EditorStorage))]
public sealed class ImageComponentPersistence : ISceneComponentPersistence
{
    private readonly EditorStorage _storage;

    public ImageComponentPersistence(EditorStorage storage)
    {
        _storage = storage;
    }

    /// <summary>Resolved on use rather than captured in the constructor — see
    /// <see cref="ProceduralComponentPersistence.Procedural"/> for why.</summary>
    private ImageSystem Images => _storage.Context.Images;

    public float Priority => 0.0f;

    public string TypeId => ImageComponent.Kind;

    public void Configure(ModelBuilder model)
    {
        model.Entity<SceneImageComponentRecord>(entity =>
        {
            entity.ToTable("wms_scene_image_components");
            entity.HasKey(record => record.EntityId);
            entity.HasOne(record => record.Entity)
                .WithOne()
                .HasForeignKey<SceneImageComponentRecord>(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public async Task LoadAsync(EditorDbContext context, IReadOnlyDictionary<int, SceneEntity> byId, IReadOnlyList<int> ids)
    {
        List<SceneImageComponentRecord> rows = await context.Set<SceneImageComponentRecord>().AsNoTracking()
            .Where(record => ids.Contains(record.EntityId))
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (SceneImageComponentRecord row in rows)
        {
            if (!byId.TryGetValue(row.EntityId, out SceneEntity? entity))
            {
                continue;
            }

            var image = new ImageComponent(Images)
            {
                ImageId = row.ImageId,
                DisplayLayerId = row.DisplayLayerId,
                WorldSizeX = (float)row.WorldSizeX,
                WorldSizeZ = (float)row.WorldSizeZ,
                Strength = (float)row.Strength,
                Channel = row.Channel,
            };
            entity.LoadComponent(image);
        }
    }

    public void Stage(EditorDbContext context, SceneEntity entity, SceneEntityRecord entityRow)
    {
        ImageComponent? image = entity.Component<ImageComponent>();
        if (image == null)
        {
            if (entity.RecordId is int id)
            {
                StageDelete(context, id);
            }

            return;
        }

        var row = new SceneImageComponentRecord
        {
            Entity = entity.RecordId is null ? entityRow : null,
            EntityId = entity.RecordId ?? 0,
            ImageId = image.ImageId,
            DisplayLayerId = image.DisplayLayerId,
            WorldSizeX = image.WorldSizeX,
            WorldSizeZ = image.WorldSizeZ,
            Strength = image.Strength,
            Channel = image.Channel,
        };
        EditorComponentPersistenceHelpers.StageRow(context, row, entity.RecordId);
    }

    public void StageDelete(EditorDbContext context, int entityId) =>
        EditorComponentPersistenceHelpers.StageDelete<SceneImageComponentRecord>(context, entityId);
}
