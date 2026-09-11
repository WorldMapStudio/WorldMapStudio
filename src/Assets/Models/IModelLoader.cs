using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>Model format loader registered under <see cref="AssetSystem"/>.</summary>
public interface IModelLoader : ISubsystem
{
    /// <summary>Which <see cref="IModelFormat"/> the assets this loader produces belong to.</summary>
    public string FormatId { get; }

    public bool CanLoad(string path);

    /// <summary>
    /// Whether <paramref name="path"/> should appear in model pickers/listings. Defaults to
    /// <see cref="CanLoad"/>; override to exclude loadable-but-not-standalone files (e.g. a format's
    /// internal LOD or group files that are only ever loaded by reference from another file).
    /// </summary>
    public bool CanList(string path) => CanLoad(path);

    /// <summary>
    /// <paramref name="work"/> is null only for the synchronous, main-thread-only <see cref="AssetSystem.LoadModelAsset"/>
    /// path; otherwise use it to hop to the main thread (<see cref="WorkContext.SwitchToMain"/>) before touching
    /// any Godot resource (e.g. building an <see cref="Godot.ArrayMesh"/>) since those aren't safe to create off it.
    /// </summary>
    public Task<ModelAsset?> LoadModelAsync(AssetSystem assets, string path, WorkContext? work);
}
