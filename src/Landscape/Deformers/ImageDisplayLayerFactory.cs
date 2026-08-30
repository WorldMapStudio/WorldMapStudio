using Godot;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>Maps <see cref="ImageDisplayLayer"/> to and from the Editor storage's
/// <c>image_display_layers</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class ImageDisplayLayerFactory(EditorStorage storage)
    : EditorCatalogFactory<ImageDisplayLayer, ImageDisplayLayerRecord>(storage)
{
    protected override DbSet<ImageDisplayLayerRecord> Set(EditorDbContext context) => context.ImageDisplayLayers;

    protected override ImageDisplayLayer ToEntity(ImageDisplayLayerRecord record) => new()
    {
        RecordId = record.Id,
        Name = record.Name,
        DisplayMode = (ImageDisplayMode)record.DisplayMode,
        OverlayColor = new Color(record.OverlayColorR, record.OverlayColorG, record.OverlayColorB, record.OverlayColorA),
    };

    protected override void WriteRecord(ImageDisplayLayer entity, ImageDisplayLayerRecord record)
    {
        record.Name = entity.Name;
        record.DisplayMode = (int)entity.DisplayMode;
        record.OverlayColorR = entity.OverlayColor.R;
        record.OverlayColorG = entity.OverlayColor.G;
        record.OverlayColorB = entity.OverlayColor.B;
        record.OverlayColorA = entity.OverlayColor.A;
    }
}
