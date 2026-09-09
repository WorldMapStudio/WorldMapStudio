namespace WorldMapStudio;

/// <summary>Where a <see cref="PaintImage"/>'s chunk pixels live. <see cref="Database"/> is the
/// original: a header row plus one <see cref="ImageChunkRecord"/> per non-empty chunk in the Editor
/// storage. <see cref="Disk"/> keeps only the header row in storage and reads/writes the pixels as
/// image files under a configured asset source — one file for a single-chunk image, one tile file per
/// chunk (named by <see cref="PaintImage.DiskTilePattern"/>) for a larger one.</summary>
public enum PaintImageStorageKind
{
    Database,
    Disk,
}
