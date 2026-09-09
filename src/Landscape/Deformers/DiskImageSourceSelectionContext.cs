using System;

namespace WorldMapStudio;

/// <summary>The disk source a <see cref="DiskImageSourceSelectionOperation"/> resolved — an asset
/// source plus a path, and the image geometry probed from the file(s) there so the creation form can
/// fill itself in.</summary>
public sealed record DiskImageSourceResult(
    string SourceId,
    string Path,
    string TilePattern,
    bool IsTiled,
    int Width,
    int Height,
    int ChunkSize,
    int Components,
    PaintImagePixelFormat Format);

public sealed record DiskImageSourceSelectionContext(
    AssetSystem Assets,
    Action<DiskImageSourceResult> Select);
