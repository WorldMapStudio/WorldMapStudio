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
    public string ProfileId { get; set; } = "";

    public int MapId { get; set; }

    public int ChunkX { get; set; }

    public int ChunkY { get; set; }

    public string ContentHash { get; set; } = "";

    public DateTime ExportedAtUtc { get; set; }
}

/// <summary>
/// A stable id an export profile has assigned to an entity — for a target format that needs one (e.g.
/// an ADT placement's unique id) but has no id of its own to reuse. Scoped per profile, keyed on the
/// entity's runtime <see cref="EntityId"/> rather than any catalog/record id, since the same entity
/// keeps this assignment across saves that change nothing else about it. See
/// <see cref="ExportSystem.LoadExportedEntityIdsAsync"/>/<see cref="ExportSystem.UpsertExportedEntityIdsAsync"/>.
/// </summary>
public sealed class ExportedEntityIdRecord
{
    public string ProfileId { get; set; } = "";

    public long EntityId { get; set; }

    public long AllocatedId { get; set; }
}
