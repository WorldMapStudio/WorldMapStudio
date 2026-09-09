using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>Storage implementation registered under <see cref="AssetSystem"/>.</summary>
public interface IAssetProvider : ISubsystem
{
    public bool Supports(string type);

    public IEnumerable<AssetRef> ListAssets(AssetSourceSettings source);

    public Task<byte[]?> ReadBytesAsync(AssetSourceSettings source, string path);

    public Task<string?> ReadTextAsync(AssetSourceSettings source, string path);

    /// <summary>Writes <paramref name="bytes"/> to <paramref name="path"/> within
    /// <paramref name="source"/>, creating the file (and any parent directories) if needed. Returns
    /// whether the write happened — a read-only provider leaves the default and returns false.</summary>
    public Task<bool> WriteBytesAsync(AssetSourceSettings source, string path, byte[] bytes) => Task.FromResult(false);

    /// <summary>Deletes <paramref name="path"/> within <paramref name="source"/> if it exists. Returns
    /// whether a file was removed. A read-only provider returns false.</summary>
    public Task<bool> DeleteAsync(AssetSourceSettings source, string path) => Task.FromResult(false);
}
