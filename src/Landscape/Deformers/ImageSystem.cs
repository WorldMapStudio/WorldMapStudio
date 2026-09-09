using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Owns the loaded <see cref="PaintImage"/> and <see cref="ImageDisplayLayer"/> catalogs. Unlike
/// <see cref="ProceduralSystem"/>, there is no build cache to bound — an <see cref="ImageComponent"/>'s
/// viewport representation is just a decal or a plane, cheap to rebuild — but it still needs the same
/// kind of per-frame <see cref="Update"/> sweep to notice a shared image or display layer changed
/// under a placement built from it.
/// </summary>
public sealed class ImageSystem : IWorldParticipant
{
    private (int CatalogVersion, int SceneVersion, int RevisionSum) _lastUpdateTick = (-1, -1, -1);

    // Published as one immutable snapshot rather than tables kept up to date in place: a landscape
    // build worker resolves an ImageComponent's bound image through FindImage while it rasterizes, so
    // whatever backs that lookup is read from many threads at once. Nothing already published is ever
    // mutated — a stale one is replaced by a freshly built one in a single reference write — so a
    // reader either sees the whole of the old snapshot or the whole of the new one.
    private sealed record CatalogIndex(
        int Version,
        IReadOnlyList<PaintImage> Images,
        IReadOnlyList<ImageDisplayLayer> Layers,
        Dictionary<int, PaintImage> ImagesById,
        Dictionary<int, ImageDisplayLayer> LayersById);

    private CatalogIndex _index = new(-1, [], [], [], []);

    public ImageSystem(EditorContext context)
    {
        Context = context;
        Residency = new ImageResidencySystem(context, this);
    }

    public EditorContext Context { get; }

    /// <summary>Keeps resident image chunks matching what streamed-in placements need and evicts the
    /// rest under a byte budget. See <see cref="ImageResidencySystem"/>.</summary>
    public ImageResidencySystem Residency { get; }

    /// <summary>The loaded image catalog. Membership comes from <see cref="EditorContext.Catalog"/>,
    /// materialized once per <see cref="CatalogEntityRegistry.Version"/>. Every read of
    /// <see cref="ImageComponent.Image"/> resolves through <see cref="FindImage"/>, and a placement
    /// reads it several times a frame, so a fresh scan of the whole catalog per read is a scan of
    /// every loaded row of every catalog type to find one image.</summary>
    public IReadOnlyList<PaintImage> Images => Index().Images;

    /// <summary>The loaded display-layer catalog. Materialized the same way <see cref="Images"/> is.</summary>
    public IReadOnlyList<ImageDisplayLayer> DisplayLayers => Index().Layers;

    public PaintImage? FindImage(int? id)
    {
        CatalogIndex index = Index();
        return id is int value ? Lookup(index.ImagesById, index.Images, value) : null;
    }

    public ImageDisplayLayer? FindDisplayLayer(int? id)
    {
        CatalogIndex index = Index();
        return id is int value ? Lookup(index.LayersById, index.Layers, value) : null;
    }

    private CatalogIndex Index()
    {
        CatalogIndex current = _index;
        int version = Context.Catalog.Version;
        if (current.Version == version)
        {
            return current;
        }

        var images = new List<PaintImage>();
        var layers = new List<ImageDisplayLayer>();
        var imagesById = new Dictionary<int, PaintImage>();
        var layersById = new Dictionary<int, ImageDisplayLayer>();

        foreach (CatalogEntity entity in Context.Catalog.Entities)
        {
            switch (entity)
            {
                case PaintImage image:
                    images.Add(image);
                    Register(imagesById, image, image.RecordId);
                    break;
                case ImageDisplayLayer layer:
                    layers.Add(layer);
                    Register(layersById, layer, layer.RecordId);
                    break;
            }
        }

        var built = new CatalogIndex(version, images, layers, imagesById, layersById);
        _index = built;
        return built;
    }

    private static void Register<TEntity>(Dictionary<int, TEntity> index, TEntity entity, int? recordId)
    {
        if (recordId is int id)
        {
            index[id] = entity;
        }
    }

    // Verified on the way out rather than trusted: a record id is assigned when an entity is first
    // saved, which the registry's membership version never sees, so an index entry can name an entity
    // whose id has since moved. A miss falls back to the same scan the lookup used to be, and does not
    // write what it finds — the snapshot it read is shared with every other thread reading it.
    private static TEntity? Lookup<TEntity>(Dictionary<int, TEntity> index, IReadOnlyList<TEntity> loaded, int id)
        where TEntity : class, IKeyedCatalogEntity
    {
        if (index.TryGetValue(id, out TEntity? cached) && cached.RecordId == id)
        {
            return cached;
        }

        foreach (TEntity candidate in loaded)
        {
            if (candidate.RecordId == id)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>How many loaded scene entities currently reference this image — what the picker and
    /// the images window show so an edit or delete does not surprise the user.</summary>
    public int UsageCount(int imageId) =>
        Context.Scene.Entities.Count(entity => entity.Component<ImageComponent>()?.ImageId == imageId);

    /// <summary>How many loaded scene entities currently reference this display layer — what the
    /// layers window shows so an edit or delete does not surprise the user.</summary>
    public int DisplayLayerUsageCount(int layerId) =>
        Context.Scene.Entities.Count(entity => entity.Component<ImageComponent>()?.DisplayLayerId == layerId);

    // Catalog rows themselves come from DatabaseSystem's own IWorldParticipant, which bulk-loads every
    // registered catalog type before anything that resolves against one — nothing else to do on load.
    // Priority still matters for UnloadWorld's ordering (WorldLifecycle unloads in exact reverse).
    float IWorldParticipant.LoadPriority => 4f;

    // A resident chunk's pixel data lives on the PaintImage catalog entity itself, so dropping the
    // catalog already drops it — nothing here needs its own eviction pass.
    void IWorldParticipant.UnloadWorld()
    {
        _lastUpdateTick = (-1, -1, -1);
        _index = new CatalogIndex(-1, [], [], [], []);
        Residency.UnloadWorld();
    }

    bool IWorldParticipant.IsBusy => Residency.IsBusy;

    /// <summary>
    /// Notices an image or display layer changed since a loaded placement last built its viewport
    /// representation — from another entity, a window, an undo, or a script — and rebuilds that
    /// placement. Called once per frame, like <see cref="ProceduralSystem.Update"/>.
    ///
    /// Guarded by the same kind of cheap running tick <see cref="ProceduralSystem.Update"/> uses, so
    /// the per-entity walk below only runs on a frame where something actually moved.
    /// </summary>
    public void Update()
    {
        // Runs first so a chunk that streams in or gets evicted this frame is reflected by the
        // ViewRevision-driven sweep below in the same frame it happened, not one frame later.
        Residency.Update();

        var tick = (Context.Catalog.Version, Context.Scene.Version, SumRevisions());
        if (tick == _lastUpdateTick)
        {
            return;
        }

        _lastUpdateTick = tick;

        foreach (SceneEntity entity in Context.Scene.Entities)
        {
            if (entity.Component<ImageComponent>() is not { } component)
            {
                continue;
            }

            if (!entity.IsRepresented || !component.NeedsRefresh)
            {
                continue;
            }

            // A different image or display layer changes what the representation *is*, so it has to be
            // rebuilt. A change to the bound image's pixels only changes what some of its chunks show,
            // and that is the case a paint stroke hits every frame it drags — patch those chunks in
            // place instead, or the cost of painting scales with the whole image rather than the brush.
            if (component.NeedsStructuralRefresh)
            {
                entity.RefreshRepresentation();
            }
            else
            {
                component.SyncChunkNodes();
            }
        }
    }

    private int SumRevisions()
    {
        int sum = 0;
        foreach (PaintImage image in Images)
        {
            sum += image.ViewRevision;
        }

        foreach (ImageDisplayLayer layer in DisplayLayers)
        {
            sum += layer.Revision;
        }

        return sum;
    }
}
