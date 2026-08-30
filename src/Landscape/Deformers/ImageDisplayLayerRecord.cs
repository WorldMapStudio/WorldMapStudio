namespace WorldMapStudio;

/// <summary>EF Core row backing an <see cref="ImageDisplayLayer"/> in the Editor storage.</summary>
public sealed class ImageDisplayLayerRecord : IKeyedRecord
{
    public int Id { get; set; }

    public string Name { get; set; } = "Display Layer";

    public int DisplayMode { get; set; }

    public float OverlayColorR { get; set; } = 1.0f;

    public float OverlayColorG { get; set; } = 0.35f;

    public float OverlayColorB { get; set; } = 0.1f;

    public float OverlayColorA { get; set; } = 1.0f;
}
