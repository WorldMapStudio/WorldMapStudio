using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Request queue and LRU store of baked model thumbnails, backing the model picker's grid view. A card
/// calls <see cref="TextureId"/> every frame it is visible; that both reads the cached texture and
/// (re-)registers the request, so scrolling away for a few frames drops it from the bake queue before
/// it ever costs a model load.
/// </summary>
public sealed class ModelThumbnailCache : IDisposable
{
    private const int MaxCachedThumbnails = 512;

    /// <summary>How many frames a queued-but-not-yet-baking request survives without being re-requested.</summary>
    private const int StaleFrames = 30;

    /// <summary>How many not-yet-baking paths may have their model load kicked off ahead of time, so the
    /// baker's own load (when it gets to them) is likely already warm in <see cref="AssetSystem"/>'s cache.</summary>
    private const int MaxPrefetch = 6;

    private sealed class Entry
    {
        public ImageTexture? Texture;
        public bool Failed;
    }

    private readonly AssetSystem _assets;
    private readonly ModelThumbnailBaker _baker;
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly LinkedList<string> _lru = new();
    private readonly Dictionary<string, LinkedListNode<string>> _lruNodes = new();
    private readonly List<string> _queue = new();
    private readonly HashSet<string> _queued = new();
    private readonly Dictionary<string, int> _lastRequestedFrame = new();
    private readonly HashSet<string> _prefetching = new();
    private string? _baking;
    private int _frame;

    public ModelThumbnailCache(AssetSystem assets, MeshMaterialSystem materials, Node owner)
    {
        _assets = assets;
        _baker = new ModelThumbnailBaker(assets, materials, owner);
    }

    /// <summary>Call once per frame before drawing any cards.</summary>
    public void BeginFrame() => _frame++;

    /// <summary>The ImGui texture handle for a model's thumbnail, or zero while it is still loading/queued.
    /// Requesting a path this way is what keeps it (or renews it) in the bake queue.</summary>
    public IntPtr TextureId(string path, out bool failed)
    {
        _lastRequestedFrame[path] = _frame;

        if (_entries.TryGetValue(path, out Entry? entry))
        {
            Touch(path);
            failed = entry.Failed;
            return entry.Texture == null ? IntPtr.Zero : (IntPtr)entry.Texture.GetRid().Id;
        }

        failed = false;
        if (path != _baking && _queued.Add(path))
        {
            _queue.Add(path);
        }

        return IntPtr.Zero;
    }

    /// <summary>Advances the bake pipeline by one step. Call once per frame after drawing the grid.</summary>
    public void Update()
    {
        DropStaleQueueEntries();

        if (_baking != null)
        {
            ModelThumbnailBakeStatus status = _baker.Poll();
            if (status == ModelThumbnailBakeStatus.Ready)
            {
                Store(_baking, _baker.TakeResult());
                _baking = null;
            }
            else if (status == ModelThumbnailBakeStatus.Failed)
            {
                StoreFailure(_baking);
                _baking = null;
            }
        }

        if (_baking == null && _queue.Count > 0)
        {
            _baking = _queue[0];
            _queue.RemoveAt(0);
            _queued.Remove(_baking);
            _baker.Begin(_baking);
        }

        Prefetch();
    }

    public void Dispose() => _baker.Dispose();

    private void DropStaleQueueEntries()
    {
        for (int i = _queue.Count - 1; i >= 0; i--)
        {
            string path = _queue[i];
            int lastRequested = _lastRequestedFrame.TryGetValue(path, out int frame) ? frame : _frame;
            if (_frame - lastRequested > StaleFrames)
            {
                _queue.RemoveAt(i);
                _queued.Remove(path);
            }
        }
    }

    private void Prefetch()
    {
        foreach (string path in _queue)
        {
            if (_prefetching.Count >= MaxPrefetch)
            {
                return;
            }

            if (!_prefetching.Add(path))
            {
                continue;
            }

            Task<ModelAsset?> task = _assets.LoadModelAssetAsync(path);
            _ = task.ContinueWith(_ => _prefetching.Remove(path), TaskScheduler.Default);
        }
    }

    private void Store(string path, ImageTexture texture)
    {
        _entries[path] = new Entry { Texture = texture };
        Touch(path);
        EvictIfNeeded();
    }

    private void StoreFailure(string path)
    {
        _entries[path] = new Entry { Failed = true };
        Touch(path);
        EvictIfNeeded();
    }

    private void Touch(string path)
    {
        if (_lruNodes.TryGetValue(path, out LinkedListNode<string>? node))
        {
            _lru.Remove(node);
        }

        _lruNodes[path] = _lru.AddFirst(path);
    }

    private void EvictIfNeeded()
    {
        while (_entries.Count > MaxCachedThumbnails && _lru.Last != null)
        {
            string oldest = _lru.Last.Value;
            _lru.RemoveLast();
            _lruNodes.Remove(oldest);
            _entries.Remove(oldest);
        }
    }
}
