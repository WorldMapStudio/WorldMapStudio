using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Editor-wide asset access. Providers expose raw project sources, while typed loaders interpret
/// asset formats without caring which provider supplied the bytes.
/// </summary>
public sealed partial class AssetSystem : ISubsystemHost
{
    private readonly EditorContext _context;
    private readonly object _textureLock = new();
    private readonly Dictionary<string, Texture2D> _textureCache = new();
    private readonly Dictionary<string, Task<Texture2D?>> _pendingTextureLoads = new();
    private int _textureCacheGeneration;
    private readonly object _modelLock = new();
    private readonly Dictionary<string, ModelAsset> _modelCache = new();
    private readonly Dictionary<string, Task<ModelAsset?>> _pendingModelLoads = new();
    private int _modelCacheGeneration;

    public AssetSystem(EditorContext context)
    {
        _context = context;
        InitializeSubsystems();
    }

    public IEnumerable<IAssetProvider> Providers => Subsystems.OfType<IAssetProvider>();
    public IEnumerable<ITextureLoader> TextureLoaders => Subsystems.OfType<ITextureLoader>();
    public IEnumerable<IModelLoader> ModelLoaders => Subsystems.OfType<IModelLoader>();

    public IReadOnlyList<AssetRef> ListTextureAssets()
    {
        var assets = new List<AssetRef>();
        var paths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (AssetSourceSettings source in ActiveSources())
        {
            foreach (IAssetProvider provider in Providers.Where(provider => provider.Supports(source.Type)))
            {
                foreach (AssetRef asset in provider.ListAssets(source))
                {
                    if (!TextureLoaders.Any(loader => loader.CanLoad(asset.Path)) || !paths.Add(asset.Path))
                    {
                        continue;
                    }

                    assets.Add(asset with { Kind = AssetKind.Texture });
                }
            }
        }

        return assets;
    }

    public IReadOnlyList<AssetRef> ListModelAssets()
    {
        var assets = new List<AssetRef>();
        var paths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (AssetSourceSettings source in ActiveSources())
        {
            foreach (IAssetProvider provider in Providers.Where(provider => provider.Supports(source.Type)))
            {
                foreach (AssetRef asset in provider.ListAssets(source))
                {
                    if (!ModelLoaders.Any(loader => loader.CanLoad(asset.Path)) || !paths.Add(asset.Path))
                    {
                        continue;
                    }

                    assets.Add(asset with { Kind = AssetKind.Model });
                }
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

        foreach (ITextureLoader loader in TextureLoaders.Where(loader => loader.CanLoad(path)))
        {
            if (loader.LoadTextureImageAsync(this, path).GetAwaiter().GetResult() is { } image)
            {
                return Cache(path, ImageTexture.CreateFromImage(image));
            }
        }

        return null;
    }

    public Texture2D? LoadTextureAsset(AssetRef asset) =>
        asset.Kind == AssetKind.Texture ? LoadTextureAsset(asset.Path) : null;

    public Task<Texture2D?> LoadTextureAssetAsync(AssetRef asset) =>
        asset.Kind == AssetKind.Texture ? LoadTextureAssetAsync(asset.Path) : Task.FromResult<Texture2D?>(null);

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

    public ModelAsset? LoadModelAsset(string path)
    {
        if (path.Length == 0)
        {
            return null;
        }

        lock (_modelLock)
        {
            if (_modelCache.TryGetValue(path, out ModelAsset? cached))
            {
                return cached;
            }
        }

        foreach (IModelLoader loader in ModelLoaders.Where(loader => loader.CanLoad(path)))
        {
            if (loader.LoadModelAsync(this, path).GetAwaiter().GetResult() is { } model)
            {
                return Cache(path, model);
            }
        }

        return null;
    }

    public ModelAsset? LoadModelAsset(AssetRef asset) =>
        asset.Kind == AssetKind.Model ? LoadModelAsset(asset.Path) : null;

    public Task<ModelAsset?> LoadModelAssetAsync(AssetRef asset) =>
        asset.Kind == AssetKind.Model ? LoadModelAssetAsync(asset.Path) : Task.FromResult<ModelAsset?>(null);

    public Task<ModelAsset?> LoadModelAssetAsync(string path)
    {
        if (path.Length == 0)
        {
            return Task.FromResult<ModelAsset?>(null);
        }

        lock (_modelLock)
        {
            if (_modelCache.TryGetValue(path, out ModelAsset? cached))
            {
                return Task.FromResult<ModelAsset?>(cached);
            }

            if (_pendingModelLoads.TryGetValue(path, out Task<ModelAsset?>? pending))
            {
                return pending;
            }

            Task<ModelAsset?> task = ScheduleModelLoad(path, _modelCacheGeneration);
            _pendingModelLoads[path] = task;
            _ = task.ContinueWith(_ =>
            {
                lock (_modelLock)
                {
                    _pendingModelLoads.Remove(path);
                }
            }, TaskScheduler.Default);
            return task;
        }
    }

    public void ClearModelCache()
    {
        lock (_modelLock)
        {
            _modelCache.Clear();
            _pendingModelLoads.Clear();
            _modelCacheGeneration++;
        }
    }

    public async Task<byte[]?> ReadAssetBytesAsync(string path)
    {
        foreach (AssetSourceSettings source in ActiveSources())
        {
            foreach (IAssetProvider provider in Providers.Where(provider => provider.Supports(source.Type)))
            {
                if (await provider.ReadBytesAsync(source, path).ConfigureAwait(false) is { } bytes)
                {
                    return bytes;
                }
            }
        }

        return null;
    }

    public async Task<string?> ReadAssetTextAsync(string path)
    {
        foreach (AssetSourceSettings source in ActiveSources())
        {
            foreach (IAssetProvider provider in Providers.Where(provider => provider.Supports(source.Type)))
            {
                if (await provider.ReadTextAsync(source, path).ConfigureAwait(false) is { } text)
                {
                    return text;
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

    private ModelAsset? Cache(string key, ModelAsset? model, int? generation = null)
    {
        if (model != null)
        {
            lock (_modelLock)
            {
                if (generation.HasValue && generation.Value != _modelCacheGeneration)
                {
                    return model;
                }

                _modelCache[key] = model;
            }
        }

        return model;
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

    private Task<ModelAsset?> ScheduleModelLoad(string path, int generation)
    {
        var completion = new TaskCompletionSource<ModelAsset?>();
        WorkQueue.Schedule("Load Model", async work =>
        {
            try
            {
                work.Step(path);
                ModelAsset? model = await LoadModelAsync(path).ConfigureAwait(false);
                await work.SwitchToMain();
                Cache(path, model, generation);
                completion.SetResult(model);
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
        foreach (ITextureLoader loader in TextureLoaders.Where(loader => loader.CanLoad(path)))
        {
            if (await loader.LoadTextureImageAsync(this, path).ConfigureAwait(false) is { } image)
            {
                return image;
            }
        }

        return null;
    }

    private async Task<ModelAsset?> LoadModelAsync(string path)
    {
        foreach (IModelLoader loader in ModelLoaders.Where(loader => loader.CanLoad(path)))
        {
            if (await loader.LoadModelAsync(this, path).ConfigureAwait(false) is { } model)
            {
                return model;
            }
        }

        return null;
    }

    private IEnumerable<AssetSourceSettings> ActiveSources() =>
        _context.Project.AssetSources.Where(source => source.Enabled);
}
