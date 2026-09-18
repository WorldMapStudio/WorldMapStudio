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

    // CPU-side decoded images, never touching the GPU - see LoadImageAsync's own doc comment. Bounded by
    // estimated byte size rather than entry count: character-section and armor textures are small, but
    // there are many of them.
    private const long ImageCacheBudgetBytes = 96L * 1024 * 1024;
    private const long ResizedImageCacheBudgetBytes = 48L * 1024 * 1024;
    private readonly object _imageLock = new();
    private readonly BoundedImageCache<string> _imageCache = new(ImageCacheBudgetBytes);
    private readonly Dictionary<string, Task<Image?>> _pendingImageLoads = new();
    private readonly object _resizedImageLock = new();
    private readonly BoundedImageCache<(string Path, Vector2I Size)> _resizedImageCache = new(ResizedImageCacheBudgetBytes);

    /// <summary>Paths that no loader could produce a model for. Cached alongside the successes so a
    /// caller that asks again every rebuild costs one dictionary lookup instead of another walk over
    /// every loader and every enabled source.</summary>
    private readonly HashSet<string> _missingModels = new();
    private int _modelCacheGeneration;

    private readonly object _assetIndexLock = new();
    private AssetIndex? _assetIndexCache;
    private Task<AssetIndex>? _assetIndexTask;

    private ModelThumbnailCache? _modelThumbnails;

    public AssetSystem(EditorContext context)
    {
        _context = context;
        InitializeSubsystems();
    }

    public EditorContext Context => _context;

    /// <summary>Shared baked-thumbnail cache and offscreen baker for every picker that wants a 3D model
    /// thumbnail (the model browser grid, the dress-up item picker) - one LRU and one viewport for the
    /// whole session, keyed only by model path, so a model already baked for one picker shows instantly
    /// in another. Session-lifetime like <see cref="_modelCache"/>; never disposed.</summary>
    public ModelThumbnailCache ModelThumbnails => _modelThumbnails ??= new ModelThumbnailCache(this, _context.MeshMaterials, _context.Root);

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
    /// the main thread. The first call against a given archive-backed source opens and indexes every
    /// archive it chains to, which can take several seconds on a large install, so callers on the
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

    /// <summary>
    /// Seeds the texture cache with an already-built <see cref="Texture2D"/> under <paramref name="path"/>,
    /// so the next <see cref="LoadTextureAssetAsync(string)"/> for that path resolves synchronously to it
    /// instead of loading again. The counterpart of <see cref="EvictTexture"/> - for a caller that already
    /// composited or otherwise built a texture off the main thread's own image cache (see
    /// <see cref="LoadImageAsync"/>) and wants the ordinary path-typed material pipeline
    /// (<c>M2Material.Texture</c>) to pick it up with no second load.
    /// </summary>
    public void AddTexture(string path, Texture2D texture)
    {
        lock (_textureLock)
        {
            _textureCache[path] = texture;
        }
    }

    /// <summary>
    /// Whether <paramref name="path"/> is already sitting in the texture cache. For a caller compositing
    /// into a path it doesn't own outright (a synthetic path a live world spawn or another preview might
    /// already be using) — check this before loading, then evict only afterward if it wasn't already
    /// there, so an entry someone else still depends on is never pulled out from under them.
    /// </summary>
    public bool IsTextureCached(string path)
    {
        lock (_textureLock)
        {
            return _textureCache.ContainsKey(path);
        }
    }

    /// <summary>
    /// The decoded CPU-side <see cref="Image"/> a texture loader produced for <paramref name="path"/>,
    /// from its own path-keyed cache - never goes through <see cref="Texture2D.GetImage"/>, so a caller
    /// compositing many of these (the plugin's own character texture compositor) can run on any number of
    /// worker threads without stalling the rendering device the way a GPU readback would. Same
    /// pending-task de-duplication as <see cref="LoadTextureAssetAsync(string)"/>.
    ///
    /// The returned image is shared and cached - callers must not mutate it in place
    /// (<c>Convert</c>/<c>Resize</c> included); work on <see cref="Image.Duplicate"/> instead, or use
    /// <see cref="LoadResizedImageAsync"/> which already does this.
    /// </summary>
    public Task<Image?> LoadImageAsync(string path)
    {
        if (path.Length == 0)
        {
            return Task.FromResult<Image?>(null);
        }

        lock (_imageLock)
        {
            if (_imageCache.TryGet(path, out Image? cached))
            {
                return Task.FromResult(cached);
            }

            if (_pendingImageLoads.TryGetValue(path, out Task<Image?>? pending))
            {
                return pending;
            }

            Task<Image?> task = ScheduleImageLoad(path);
            _pendingImageLoads[path] = task;
            _ = task.ContinueWith(_ =>
            {
                lock (_imageLock)
                {
                    _pendingImageLoads.Remove(path);
                }
            }, TaskScheduler.Default);
            return task;
        }
    }

    /// <summary>
    /// The image <see cref="LoadImageAsync"/> would produce for <paramref name="path"/>, converted to
    /// RGBA8 and resized to <paramref name="size"/>, from a cache keyed by both - the same armor texture
    /// always lands in the same composited region, so repeated candidates (e.g. every dress-up thumbnail
    /// for one outfit) don't pay for <see cref="Image.Resize"/> again. The conversion/resize runs on a
    /// duplicate of the cached raw image, never mutating it; the resized result, once cached, is handed
    /// out directly since every consumer (<c>Image.BlendRect</c>) only reads from it.
    /// </summary>
    public async Task<Image?> LoadResizedImageAsync(string path, Vector2I size)
    {
        if (path.Length == 0)
        {
            return null;
        }

        (string, Vector2I) key = (path, size);
        lock (_resizedImageLock)
        {
            if (_resizedImageCache.TryGet(key, out Image? cached))
            {
                return cached;
            }
        }

        if (await LoadImageAsync(path).ConfigureAwait(false) is not { } source)
        {
            return null;
        }

        Image resized = (Image)source.Duplicate();
        if (resized.GetFormat() != Image.Format.Rgba8)
        {
            resized.Convert(Image.Format.Rgba8);
        }

        if (resized.GetWidth() != size.X || resized.GetHeight() != size.Y)
        {
            resized.Resize(size.X, size.Y);
        }

        lock (_resizedImageLock)
        {
            // A concurrent caller may have raced this one to the same key - both results are equivalent,
            // so whichever got here first wins rather than overwriting.
            if (!_resizedImageCache.TryGet(key, out Image? existing))
            {
                _resizedImageCache.Set(key, resized);
                existing = resized;
            }

            return existing;
        }
    }

    private Task<Image?> ScheduleImageLoad(string path)
    {
        var completion = new TaskCompletionSource<Image?>();
        WorkQueue.Schedule("Load Image", async work =>
        {
            try
            {
                work.Step(path);
                Image? image = await LoadTextureImageAsync(work, path).ConfigureAwait(false);
                if (image != null)
                {
                    lock (_imageLock)
                    {
                        _imageCache.Set(path, image);
                    }
                }

                completion.SetResult(image);
            }
            catch (System.Exception e)
            {
                completion.SetException(e);
                throw;
            }
        });
        return completion.Task;
    }

    /// <summary>A path-keyed LRU store of decoded images bounded by estimated byte size rather than entry
    /// count - see <see cref="AssetSystem"/>'s own image cache fields for why. Not thread-safe on its
    /// own; every call site takes its own lock around it, the same shape <c>_textureLock</c> already
    /// establishes for the GPU texture cache.</summary>
    private sealed class BoundedImageCache<TKey> where TKey : notnull
    {
        private readonly long _maxBytes;
        private readonly Dictionary<TKey, Image> _entries = new();
        private readonly LinkedList<TKey> _lru = new();
        private readonly Dictionary<TKey, LinkedListNode<TKey>> _lruNodes = new();
        private long _bytes;

        public BoundedImageCache(long maxBytes) => _maxBytes = maxBytes;

        public bool TryGet(TKey key, out Image? image)
        {
            if (_entries.TryGetValue(key, out image))
            {
                Touch(key);
                return true;
            }

            return false;
        }

        public void Set(TKey key, Image image)
        {
            if (_entries.TryGetValue(key, out Image? existing))
            {
                _bytes -= EstimateBytes(existing);
            }

            _entries[key] = image;
            _bytes += EstimateBytes(image);
            Touch(key);
            EvictIfNeeded();
        }

        private void Touch(TKey key)
        {
            if (_lruNodes.TryGetValue(key, out LinkedListNode<TKey>? node))
            {
                _lru.Remove(node);
            }

            _lruNodes[key] = _lru.AddFirst(key);
        }

        private void EvictIfNeeded()
        {
            while (_bytes > _maxBytes && _lru.Last != null)
            {
                TKey oldest = _lru.Last.Value;
                _lru.RemoveLast();
                _lruNodes.Remove(oldest);
                if (_entries.Remove(oldest, out Image? removed))
                {
                    _bytes -= EstimateBytes(removed);
                }
            }
        }

        private static long EstimateBytes(Image image) => (long)image.GetWidth() * image.GetHeight() * 4;
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

            if (_missingModels.Contains(path))
            {
                return null;
            }
        }

        foreach (IModelLoader loader in ModelLoaders.Where(loader => loader.CanLoad(path)))
        {
            if (loader.LoadModelAsync(this, path, null).GetAwaiter().GetResult() is { } model)
            {
                return Cache(path, model);
            }
        }

        return Cache(path, (ModelAsset?)null);
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

            // A completed task, so a caller polling with IsCompletedSuccessfully settles on "no model"
            // instead of scheduling a fresh load for a path already known to have none.
            if (_missingModels.Contains(path))
            {
                return Task.FromResult<ModelAsset?>(null);
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
            _missingModels.Clear();
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
        lock (_modelLock)
        {
            if (generation.HasValue && generation.Value != _modelCacheGeneration)
            {
                return model;
            }

            if (model != null)
            {
                _modelCache[key] = model;
            }
            else
            {
                _missingModels.Add(key);
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
                ModelAsset? model = await LoadModelAsync(path, work).ConfigureAwait(false);
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

    private async Task<ModelAsset?> LoadModelAsync(string path, WorkContext work)
    {
        foreach (IModelLoader loader in ModelLoaders.Where(loader => loader.CanLoad(path)))
        {
            if (await loader.LoadModelAsync(this, path, work).ConfigureAwait(false) is { } model)
            {
                return model;
            }
        }

        return null;
    }

    private IEnumerable<AssetSourceSettings> ActiveSources() =>
        _context.Project.AssetSources.Where(source => source.Enabled);
}
