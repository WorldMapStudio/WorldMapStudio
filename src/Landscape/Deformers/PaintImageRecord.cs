using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>EF Core row backing a <see cref="PaintImage"/>'s header in the Editor storage — its pixel
/// data lives separately, one row per non-empty chunk, in <see cref="ImageChunkRecord"/>.</summary>
public sealed class PaintImageRecord : IKeyedRecord
{
    public int Id { get; set; }

    public string Name { get; set; } = "Image";

    public int Width { get; set; } = 256;

    public int Height { get; set; } = 256;

    public int ChunkSize { get; set; } = 256;

    public int Components { get; set; } = 1;

    /// <summary>One of <see cref="PaintImagePixelFormat"/>.</summary>
    public int PixelFormat { get; set; }

    /// <summary>One of <see cref="PaintImageStorageKind"/> — 0 (<see cref="PaintImageStorageKind.Database"/>)
    /// for every image predating disk storage.</summary>
    public int StorageKind { get; set; }

    /// <summary>A disk-backed image's absolute path — the image file (single chunk) or the tile
    /// directory (multi-chunk); null for a database image.</summary>
    public string? DiskPath { get; set; }

    /// <summary>Tile file name pattern for a multi-chunk disk-backed image; null otherwise.</summary>
    public string? DiskTilePattern { get; set; }
}

/// <summary>EF Core row for one non-empty chunk of a <see cref="PaintImage"/>. A chunk coordinate with
/// no row here is entirely zero — see <see cref="ImageChunkTable"/>.</summary>
public sealed class ImageChunkRecord
{
    public int ImageId { get; set; }

    public int ChunkX { get; set; }

    public int ChunkY { get; set; }

    /// <summary>One of <see cref="ImageChunkCodec.FormatRaw"/> / <see cref="ImageChunkCodec.FormatDeflate"/>.</summary>
    public byte Format { get; set; }

    public byte[] Pixels { get; set; } = [];
}

public sealed partial class EditorDbContext
{
    public DbSet<PaintImageRecord> Images => Set<PaintImageRecord>();

    public DbSet<ImageChunkRecord> ImageChunks => Set<ImageChunkRecord>();
}
