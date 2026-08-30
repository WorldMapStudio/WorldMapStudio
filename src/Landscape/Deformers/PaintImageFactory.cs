using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>Maps <see cref="PaintImage"/> to and from the Editor storage's <c>images</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class PaintImageFactory(EditorStorage storage)
    : EditorCatalogFactory<PaintImage, PaintImageRecord>(storage)
{
    protected override DbSet<PaintImageRecord> Set(EditorDbContext context) => context.Images;

    protected override PaintImage ToEntity(PaintImageRecord record)
    {
        var entity = new PaintImage { RecordId = record.Id, Name = record.Name };
        entity.LoadPixels(record.Width, record.Height, record.Pixels);
        return entity;
    }

    protected override void WriteRecord(PaintImage entity, PaintImageRecord record)
    {
        record.Name = entity.Name;
        record.Width = entity.Width;
        record.Height = entity.Height;
        record.Pixels = entity.CopyPixels();
    }
}
