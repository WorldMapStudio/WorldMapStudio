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

    public ImageSystem(EditorContext context)
    {
        Context = context;
        Residency = new ImageResidencySystem(context, this);
    }

    public EditorContext Context { get; }

    /// <summary>Keeps resident image chunks matching what streamed-in placements need and evicts the
    /// rest under a byte budget. See <see cref="ImageResidencySystem"/>.</summary>
    public ImageResidencySystem Residency { get; }

    /// <summary>The loaded image catalog. Membership comes from <see cref="EditorContext.Catalog"/>.</summary>
    public IEnumerable<PaintImage> Images => Context.Catalog.OfType<PaintImage>();

    /// <summary>The loaded display-layer catalog. Membership comes from <see cref="EditorContext.Catalog"/>.</summary>
    public IEnumerable<ImageDisplayLayer> DisplayLayers => Context.Catalog.OfType<ImageDisplayLayer>();

    public PaintImage? FindImage(int? id) =>
        id is int value ? Images.FirstOrDefault(image => image.RecordId == value) : null;

    public ImageDisplayLayer? FindDisplayLayer(int? id) =>
        id is int value ? DisplayLayers.FirstOrDefault(layer => layer.RecordId == value) : null;

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
