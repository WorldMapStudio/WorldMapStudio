using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>Loader implementation registered under <see cref="AssetSystem"/>.</summary>
public interface IAssetProvider : ISubsystem
{
    public bool Supports(AssetSourceType type);

    public IEnumerable<AssetRef> ListTextureAssets(AssetSourceSettings source);

    public Task<Image?> LoadTextureImageAsync(AssetSourceSettings source, string path);

    public Texture2D? LoadTextureAsset(AssetSourceSettings source, string path);
}
