using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>Storage implementation registered under <see cref="AssetSystem"/>.</summary>
public interface IAssetProvider : ISubsystem
{
    public bool Supports(AssetSourceType type);

    public IEnumerable<AssetRef> ListAssets(AssetSourceSettings source);

    public Task<byte[]?> ReadBytesAsync(AssetSourceSettings source, string path);

    public Task<string?> ReadTextAsync(AssetSourceSettings source, string path);
}
