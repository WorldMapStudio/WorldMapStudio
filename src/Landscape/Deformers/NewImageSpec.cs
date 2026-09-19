namespace WorldMapStudio;

/// <summary>What <see cref="ImageSystem.BuildCreateCommand"/> needs to make a <see cref="PaintImage"/>.
/// Sizes are in pixels. <see cref="DiskPath"/> backs the image with files instead of the database.</summary>
public sealed record NewImageSpec
{
    /// <summary>The image's id, or null to take the next free one.</summary>
    public int? RecordId { get; init; }

    public string Name { get; init; } = "Image";

    public int Width { get; init; }

    public int Height { get; init; }

    public int ChunkSize { get; init; }

    public int Components { get; init; } = 1;

    public PaintImagePixelFormat Format { get; init; } = PaintImagePixelFormat.Byte;

    public string? DiskPath { get; init; }

    /// <summary>The tile pattern of a tiled <see cref="DiskPath"/> folder, empty for a single file.</summary>
    public string DiskTilePattern { get; init; } = "";
}
