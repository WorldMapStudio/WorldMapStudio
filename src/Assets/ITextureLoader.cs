using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>Texture format loader registered under <see cref="AssetSystem"/>.</summary>
public interface ITextureLoader : ISubsystem
{
    public bool CanLoad(string path);

    public Task<Image?> LoadTextureImageAsync(AssetSystem assets, string path);
}
