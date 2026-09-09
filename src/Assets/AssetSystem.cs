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
    private readonly record struct AssetIndex(IReadOnlyList<AssetRef> Textures, IReadOnlyList<AssetRef> Models);

    private readonly EditorContext _context;
    private readonly object _textureLock = new();
    private readonly Dictionary<string, Texture2D> _textureCache = new();
    private readonly Dictionary<string, Task<Texture2D?>> _pendingTextureLoads = new();
    private int _textureCacheGeneration;
    private readonly object _modelLock = new();
    private readonly Dictionary<string, ModelAsset> _modelCache = new();
    private readonly Dictionary<string, Task<ModelAsset?>> _pendingModelLoads = new();
    private int _modelCacheGeneration;

    private readonly object _assetIndexLock = new();
    private AssetIndex? _assetIndexCache;
    private Task<AssetIndex>? _assetIndexTask;

    public AssetSystem(EditorContext context)
    {
        _context = context;
        InitializeSubsystems();
    }

    public EditorContext Context => _context;

    public IEnumerable<IAssetProvider> Providers => Subsystems.OfType<IAssetProvider>();
    public IEnumerable<ITextureLoader> TextureLoaders => Subsystems.OfType<ITextureLoader>();
    public IEnumerable<IModelLoader> ModelLoaders => Subsystems.OfType<IModelLoader>();

    /// <summary>The loader that would handle <paramref name="path"/>, e.g. to query its transform capabilities.</summary>
    public IModelLoader? FindModelLoader(string path) => ModelLoaders.FirstOrDefault(loader => loader.CanLoad(path));

    /// <summary>The format <paramref name="path"/> would load as, e.g. to query its placement constraints.</summary>
    public IModelFormat? FindModelFormat(string path) =>
        FindModelLoader(path) is { } loader ? _context.ModelFormats.Find(loader.FormatId) : null;

    public IReadOnlyList<AssetRef> ListTextureAssets() => BuildAssetIndex().Textures;

    public IReadOnlyList<AssetRef> ListModelAssets() => BuildAssetIndex().Models;

    /// <summary>
    /// Same as <see cref="ListTextureAssets"/> and <see cref="ListModelAssets"/> combined, but runs off
    /// the main thread. The first call against a given MPQ source opens and indexes every archive in
    /// its patch chain, which can take several seconds on a full retail install, so callers on the
    /// render loop must not do this synchronously. Texture and model listings share one provider scan
    /// (and one cache) since they are both just different filters over the same underlying paths.
    /// </summary>
    public async Task<IReadOnlyList<AssetRef>> ListTextureAssetsAsync() => (await ListAssetIndexAsync().ConfigureAwait(false)).Textures;

    /// <summary>See <see cref="ListTextureAssetsAsync"/>; the model equivalent.</summary>
    public async Task<IReadOnlyList<AssetRef>> ListModelAssetsAsync() => (await ListAssetIndexAsync().ConfigureAwait(false)).Models;

    private Task<AssetIndex> ListAssetIndexAsync()
    {
        lock (_assetIndexLock)
        {
            if (_assetIndexCache is { } cached)
            {
                return Task.FromResult(cached);
            }

            if (_assetIndexTask is { } pending)
            {
                return pending;
            }

            var completion = new TaskCompletionSource<AssetIndex>();
            WorkQueue.Schedule("Index Assets", _ =>
            {
                AssetIndex result = BuildAssetIndex();
                lock (_assetIndexLock)
                {
                    _assetIndexCache = result;
                    _assetIndexTask = null;
                }

                completion.SetResult(result);
            });
            _assetIndexTask = completion.Task;
            return _assetIndexTask;
        }
    }

    /// <summary>
    /// Walks every active provider's asset list exactly once, sorting each path into the texture
    /// and/or model bucket by asking each format loader — instead of the equivalent of two full,
    /// independent scans (one per kind), which used to double the cost of the same underlying walk.
    /// </summary>
    private AssetIndex BuildAssetIndex()
    {
        lock (_assetIndexLock)
        {
            if (_assetIndexCache is { } cached)
            {
                return cached;
            }
        }

        var textures = new List<AssetRef>();
        var models = new List<AssetRef>();
        var textureSeen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var modelSeen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (AssetSourceSettings source in ActiveSources())
        {
            foreach (IAssetProvider provider in Providers.Where(provider => provider.Supports(source.Type)))
            {
                foreach (AssetRef asset in provider.ListAssets(source))
                {
                    if (TextureLoaders.Any(loader => loader.CanLoad(asset.Path)) && textureSeen.Add(asset.Path))
                    {
                        textures.Add(asset with { Kind = AssetKind.Texture });
                    }

                    if (ModelLoaders.Any(loader => loader.CanList(asset.Path)) && modelSeen.Add(asset.Path))
                    {
                        models.Add(asset with { Kind = AssetKind.Model });
                    }
                }
            }
        }

        var result = new AssetIndex(textures, models);
        lock (_assetIndexLock)
        {
            _assetIndexCache ??= result;
            return _assetIndexCache.Value;
        }
    }

    private void ClearAssetIndex()
    {
        lock (_assetIndexLock)
        {
            _assetIndexCache = null;
            _assetIndexTask = null;
        }
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

    /// <summary>
    /// Drops one cached texture, so the next <see cref="LoadTextureAssetAsync(string)"/> for that path
    /// decodes it again. The narrow counterpart of <see cref="ClearTextureCache"/>, which also throws
    /// away every other texture <i>and</i> the asset index — far too blunt for a caller that knows
    /// exactly which entry went stale.
    ///
    /// Exists for synthetic, generated textures (see the plugin's own NPC compositor) whose content is
    /// a function of editor state rather than of a file on disk: a live editor can mint many of them in
    /// one session, and without this they would accumulate until a world reload. A path backed by a
    /// real asset rarely needs it — that content does not change underneath the cache.
    /// </summary>
    public void EvictTexture(string path)
    {
        lock (_textureLock)
        {
            _textureCache.Remove(path);
            _pendingTextureLoads.Remove(path);
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

        ClearAssetIndex();
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

        ClearAssetIndex();
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

    /// <summary>Reads an asset from one named source only, rather than the first active source that
    /// happens to resolve <paramref name="path"/> — what a disk-backed <see cref="PaintImage"/> uses so
    /// its tiles always come from the source it was configured against.</summary>
    public async Task<byte[]?> ReadAssetBytesFromAsync(string sourceId, string path)
    {
        if (FindSource(sourceId) is not { } source)
        {
            return null;
        }

        foreach (IAssetProvider provider in Providers.Where(provider => provider.Supports(source.Type)))
        {
            if (await provider.ReadBytesAsync(source, path).ConfigureAwait(false) is { } bytes)
            {
                return bytes;
            }
        }

        return null;
    }

    /// <summary>Writes an asset into one named source. Returns whether a provider took the write.</summary>
    public async Task<bool> WriteAssetBytesAsync(string sourceId, string path, byte[] bytes)
    {
        if (FindSource(sourceId) is not { } source)
        {
            return false;
        }

        foreach (IAssetProvider provider in Providers.Where(provider => provider.Supports(source.Type)))
        {
            if (await provider.WriteBytesAsync(source, path, bytes).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Deletes an asset from one named source. Returns whether a file was removed.</summary>
    public async Task<bool> DeleteAssetAsync(string sourceId, string path)
    {
        if (FindSource(sourceId) is not { } source)
        {
            return false;
        }

        foreach (IAssetProvider provider in Providers.Where(provider => provider.Supports(source.Type)))
        {
            if (await provider.DeleteAsync(source, path).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    private AssetSourceSettings? FindSource(string sourceId) =>
        ActiveSources().FirstOrDefault(source => source.Id == sourceId);

    /// <summary>Every asset path in one named source, optionally restricted to those under
    /// <paramref name="underDirectory"/> (a source-relative prefix). Runs off the main thread — a
    /// provider's listing can open and index archives — so callers on the render loop must await it.</summary>
    public Task<IReadOnlyList<string>> ListSourcePathsAsync(string sourceId, string underDirectory = "")
    {
        string prefix = underDirectory.Length == 0 ? "" : AssetPath.Normalize(underDirectory).TrimEnd('/') + "/";
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            if (FindSource(sourceId) is not { } source)
            {
                return [];
            }

            var paths = new List<string>();
            foreach (IAssetProvider provider in Providers.Where(provider => provider.Supports(source.Type)))
            {
                foreach (AssetRef asset in provider.ListAssets(source))
                {
                    string normalized = AssetPath.Normalize(asset.Path);
                    if (prefix.Length == 0 || normalized.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    {
                        paths.Add(normalized);
                    }
                }
            }

            return paths;
        });
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
