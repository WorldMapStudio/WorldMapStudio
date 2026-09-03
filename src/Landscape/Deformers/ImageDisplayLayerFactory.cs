using Godot;

namespace WorldMapStudio;

/// <summary>Maps <see cref="ImageDisplayLayer"/> to and from the Editor storage's
/// <c>image_display_layers</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class ImageDisplayLayerFactory(EditorStorage storage)
    : EditorCatalogFactory<ImageDisplayLayer, ImageDisplayLayerRecord>(storage)
{
    protected override string TableName => "image_display_layers";

    protected override ImageDisplayLayer ToEntity(ImageDisplayLayerRecord record) => new()
    {
        RecordId = record.Id,
        Name = record.Name,
        DisplayMode = (ImageDisplayMode)record.DisplayMode,
        ColorSource = (ImageColorSource)record.ColorSource,
        BaseColor = new Color(record.BaseColorR, record.BaseColorG, record.BaseColorB, record.BaseColorA),
        FullColor = new Color(record.FullColorR, record.FullColorG, record.FullColorB, record.FullColorA),
    };

    protected override void WriteRecord(ImageDisplayLayer entity, ImageDisplayLayerRecord record)
    {
        record.Name = entity.Name;
        record.DisplayMode = (int)entity.DisplayMode;
        record.ColorSource = (int)entity.ColorSource;
        record.BaseColorR = entity.BaseColor.R;
        record.BaseColorG = entity.BaseColor.G;
        record.BaseColorB = entity.BaseColor.B;
        record.BaseColorA = entity.BaseColor.A;
        record.FullColorR = entity.FullColor.R;
        record.FullColorG = entity.FullColor.G;
        record.FullColorB = entity.FullColor.B;
        record.FullColorA = entity.FullColor.A;
    }
}
