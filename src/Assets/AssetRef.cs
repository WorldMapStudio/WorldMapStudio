namespace WorldMapStudio;

public sealed record AssetRef(
    AssetKind Kind,
    string SourceId,
    string SourceName,
    string Path,
    string DisplayName,
    string FullPath);
