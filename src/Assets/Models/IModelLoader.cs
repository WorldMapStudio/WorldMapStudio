using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>Model format loader registered under <see cref="AssetSystem"/>.</summary>
public interface IModelLoader : ISubsystem
{
    public bool CanLoad(string path);

    /// <summary>
    /// Whether <paramref name="path"/> should appear in model pickers/listings. Defaults to
    /// <see cref="CanLoad"/>; override to exclude loadable-but-not-standalone files (e.g. a format's
    /// internal LOD or group files that are only ever loaded by reference from another file).
    /// </summary>
    public bool CanList(string path) => CanLoad(path);

    /// <summary>How placed instances of this format may rotate about themselves. Default: unrestricted.</summary>
    public SelfRotation SelfRotation => SelfRotation.Full;

    /// <summary>How placed instances of this format may be scaled. Default: unrestricted per-axis.</summary>
    public SelfScale SelfScale => SelfScale.PerAxis;

    public Task<ModelAsset?> LoadModelAsync(AssetSystem assets, string path);
}
