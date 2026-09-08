namespace WorldMapStudio;

/// <summary>
/// A per-map catalog entity that chunks are resolved against — a channel, a layer, a material. Editing
/// one can move the built output of any chunk on its map without touching a single scene entity, so
/// <see cref="ChunkChangeLog.RecordCommit"/> stamps the whole map's chunks when a committed edit
/// targets one. A marker rather than a type list so a plugin's own landscape catalog type gets the
/// same treatment.
/// </summary>
public interface ILandscapeCatalogEntity
{
    /// <summary>The map this entity belongs to.</summary>
    MapId Map { get; }
}
