namespace WorldMapStudio;

/// <summary>EF Core row backing a <see cref="PaintImage"/> in the Editor storage.</summary>
public sealed class PaintImageRecord : IKeyedRecord
{
    public int Id { get; set; }

    public string Name { get; set; } = "Image";

    public int Width { get; set; } = 256;

    public int Height { get; set; } = 256;

    public byte[] Pixels { get; set; } = [];
}
