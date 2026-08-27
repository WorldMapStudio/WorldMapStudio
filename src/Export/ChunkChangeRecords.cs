using System;

namespace WorldMapStudio;

public sealed class ChunkChangeRecord
{
    public int MapId { get; set; }

    public int ChunkX { get; set; }

    public int ChunkY { get; set; }

    public string ContentHash { get; set; } = "";

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ExportedChunkRecord
{
    public string ExporterId { get; set; } = "";

    public int MapId { get; set; }

    public int ChunkX { get; set; }

    public int ChunkY { get; set; }

    public string ContentHash { get; set; } = "";

    public DateTime ExportedAtUtc { get; set; }
}
