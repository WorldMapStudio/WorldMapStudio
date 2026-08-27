namespace WorldMapStudio;

/// <summary>
/// One configured place to look for assets. Kept as a list on the project so users can add more than
/// one source of the same type, for example several filesystem texture folders.
/// </summary>
public sealed class AssetSourceSettings
{
    public string Id { get; set; } = "assets";

    public string Name { get; set; } = "Assets";

    public AssetSourceType Type { get; set; } = AssetSourceType.FileSystem;

    public bool Enabled { get; set; } = true;

    public string RootPath { get; set; } = "";
}
