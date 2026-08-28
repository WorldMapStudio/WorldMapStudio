using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>Model format loader registered under <see cref="AssetSystem"/>.</summary>
public interface IModelLoader : ISubsystem
{
    public bool CanLoad(string path);

    public Task<ModelAsset?> LoadModelAsync(AssetSystem assets, string path);
}
