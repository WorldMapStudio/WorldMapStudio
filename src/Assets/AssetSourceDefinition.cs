namespace WorldMapStudio;

public interface IAssetSourceDefinition
{
    public string Type { get; }

    public string Label { get; }

    public string DefaultIdPrefix { get; }

    public string DefaultNamePrefix { get; }

    public void DrawSettings(AssetSourceSettings source);
}
