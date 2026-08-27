using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Editor-wide asset access. Providers register as subsystems, while the project supplies any number
/// of configured source instances for those providers to read from.
/// </summary>
public sealed partial class AssetSystem : ISubsystemHost
{
    private readonly EditorContext _context;
    private readonly object _textureLock = new();
    private readonly Dictionary<string, Texture2D> _textureCache = new();
    private readonly Dictionary<string, Task<Texture2D?>> _pendingTextureLoads = new();
    private int _textureCacheGeneration;

    public AssetSystem(EditorContext context)
    {
        _context = context;
        InitializeSubsystems();
    }

    public IEnumerable<IAssetProvider> Providers => Subsystems.Cast<IAssetProvider>();

    public IReadOnlyList<AssetRef> ListTextureAssets()
    {
        var assets = new List<AssetRef>();
        foreach (AssetSourceSettings source in ActiveSources())
        {
            foreach (IAssetProvider provider in Providers.Where(provider => provider.Supports(source.Type)))
            {
                assets.AddRange(provider.ListTextureAssets(source));
            }
        }

        return assets;
    }

    public Texture2D? LoadTextureAsset(string path)
    {
        if (path.Length == 0)
        {
            return null;
        }

        lock (_textureLock)
        {
            if (_textureCache.TryGetValue(path, out Texture2D? cached))
            {
                return cached;
            }
        }

        if (TrySplitQualifiedPath(path, out string sourceName, out string sourcePath))
        {
            return Cache(path, LoadTextureAsset(sourceName, sourcePath));
        }

        foreach (AssetSourceSettings source in ActiveSources())
        {
            foreach (IAssetProvider provider in Providers.Where(provider => provider.Supports(source.Type)))
            {
                if (provider.LoadTextureAsset(source, path) is { } texture)
                {
                    return Cache(path, texture);
                }
            }
        }

        if (ResourceLoader.Exists(path))
        {
            return Cache(path, ResourceLoader.Load<Texture2D>(path));
        }

        return null;
    }

    public Texture2D? LoadTextureAsset(AssetRef asset) =>
        asset.Kind == AssetKind.Texture ? LoadTextureAsset(asset.QualifiedPath) : null;

    public Task<Texture2D?> LoadTextureAssetAsync(AssetRef asset) =>
        asset.Kind == AssetKind.Texture ? LoadTextureAssetAsync(asset.QualifiedPath) : Task.FromResult<Texture2D?>(null);

    public Task<Texture2D?> LoadTextureAssetAsync(string path)
    {
        if (path.Length == 0)
        {
            return Task.FromResult<Texture2D?>(null);
        }

        lock (_textureLock)
        {
            if (_textureCache.TryGetValue(path, out Texture2D? cached))
            {
                return Task.FromResult<Texture2D?>(cached);
            }

            if (_pendingTextureLoads.TryGetValue(path, out Task<Texture2D?>? pending))
            {
                return pending;
            }

            Task<Texture2D?> task = ScheduleTextureLoad(path, _textureCacheGeneration);
            _pendingTextureLoads[path] = task;
            _ = task.ContinueWith(_ =>
            {
                lock (_textureLock)
                {
                    _pendingTextureLoads.Remove(path);
                }
            }, TaskScheduler.Default);
            return task;
        }
    }

    public void ClearTextureCache()
    {
        lock (_textureLock)
        {
            _textureCache.Clear();
            _pendingTextureLoads.Clear();
            _textureCacheGeneration++;
        }
    }

    private Texture2D? LoadTextureAsset(string sourceIdOrName, string path)
    {
        foreach (AssetSourceSettings source in ActiveSources().Where(source =>
            source.Id == sourceIdOrName || source.Name == sourceIdOrName))
        {
            foreach (IAssetProvider provider in Providers.Where(provider => provider.Supports(source.Type)))
            {
                if (provider.LoadTextureAsset(source, path) is { } texture)
                {
                    return texture;
                }
            }
        }

        return null;
    }

    private Texture2D? Cache(string key, Texture2D? texture, int? generation = null)
    {
        if (texture != null)
        {
            lock (_textureLock)
            {
                if (generation.HasValue && generation.Value != _textureCacheGeneration)
                {
                    return texture;
                }

                _textureCache[key] = texture;
            }
        }

        return texture;
    }

    private Task<Texture2D?> ScheduleTextureLoad(string path, int generation)
    {
        var completion = new TaskCompletionSource<Texture2D?>();
        WorkQueue.Schedule("Load Texture", async work =>
        {
            try
            {
                work.Step(path);
                Image? image = await LoadTextureImageAsync(work, path).ConfigureAwait(false);
                if (image == null)
                {
                    await work.SwitchToMain();
                    completion.SetResult(null);
                    return;
                }

                await work.SwitchToMain();
                Texture2D texture = ImageTexture.CreateFromImage(image);
                Cache(path, texture, generation);
                completion.SetResult(texture);
            }
            catch (System.Exception e)
            {
                completion.SetException(e);
                throw;
            }
        });
        return completion.Task;
    }

    private async Task<Image?> LoadTextureImageAsync(WorkContext work, string path)
    {
        if (TrySplitQualifiedPath(path, out string sourceName, out string sourcePath))
        {
            return await LoadTextureImageAsync(sourceName, sourcePath).ConfigureAwait(false);
        }

        foreach (AssetSourceSettings source in ActiveSources())
        {
            foreach (IAssetProvider provider in Providers.Where(provider => provider.Supports(source.Type)))
            {
                if (await provider.LoadTextureImageAsync(source, path).ConfigureAwait(false) is { } image)
                {
                    return image;
                }
            }
        }

        await work.SwitchToMain();
        if (ResourceLoader.Exists(path) && ResourceLoader.Load<Texture2D>(path) is { } texture)
        {
            return texture.GetImage();
        }

        return null;
    }

    private async Task<Image?> LoadTextureImageAsync(string sourceIdOrName, string path)
    {
        foreach (AssetSourceSettings source in ActiveSources().Where(source =>
            source.Id == sourceIdOrName || source.Name == sourceIdOrName))
        {
            foreach (IAssetProvider provider in Providers.Where(provider => provider.Supports(source.Type)))
            {
                if (await provider.LoadTextureImageAsync(source, path).ConfigureAwait(false) is { } image)
                {
                    return image;
                }
            }
        }

        return null;
    }

    private IEnumerable<AssetSourceSettings> ActiveSources() =>
        _context.Project.AssetSources.Where(source => source.Enabled);

    private static bool TrySplitQualifiedPath(string path, out string sourceName, out string sourcePath)
    {
        int separator = path.IndexOf("::", System.StringComparison.Ordinal);
        if (separator <= 0)
        {
            sourceName = "";
            sourcePath = "";
            return false;
        }

        sourceName = path[..separator];
        sourcePath = path[(separator + 2)..];
        return sourcePath.Length > 0;
    }
}
