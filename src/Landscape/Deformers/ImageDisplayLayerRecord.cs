namespace WorldMapStudio;

/// <summary>EF Core row backing an <see cref="ImageDisplayLayer"/> in the Editor storage.</summary>
public sealed class ImageDisplayLayerRecord : IKeyedRecord
{
    public int Id { get; set; }

    public string Name { get; set; } = "Display Layer";

    public int DisplayMode { get; set; }

    public int ColorSource { get; set; }

    public float BaseColorR { get; set; } = 1.0f;

    public float BaseColorG { get; set; } = 0.35f;

    public float BaseColorB { get; set; } = 0.1f;

    public float BaseColorA { get; set; }

    public float FullColorR { get; set; } = 1.0f;

    public float FullColorG { get; set; } = 0.35f;

    public float FullColorB { get; set; } = 0.1f;

    public float FullColorA { get; set; } = 1.0f;
}
