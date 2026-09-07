using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// What a deformer needs loaded before it can rasterize offline, declared before anything is fetched
/// so every deformer's needs are satisfied as one batch. Built and driven by
/// <see cref="LandscapeBuildPreparation"/>.
/// </summary>
public sealed class LandscapeBuildRequest
{
    private readonly Dictionary<PaintImage, HashSet<ImageChunkCoord>> _imageChunks = [];
    private readonly HashSet<IPreparableLandscapeDeformer> _mainThread = [];
    private IPreparableLandscapeDeformer? _current;

    internal LandscapeBuildRequest(Aabb region) => Region = region;

    /// <summary>The block being built, grown by the halo — what a footprint is clipped against.</summary>
    public Aabb Region { get; }

    internal IReadOnlyDictionary<PaintImage, HashSet<ImageChunkCoord>> WantedImageChunks => _imageChunks;

    internal bool WantsMainThread(IPreparableLandscapeDeformer deformer) => _mainThread.Contains(deformer);

    internal bool AnythingRequested => _imageChunks.Count > 0 || _mainThread.Count > 0;

    /// <summary>Set by the driver before each deformer's <see cref="IPreparableLandscapeDeformer.Request"/>
    /// so the request knows which deformer a <see cref="RequiresMainThread"/> call belongs to.</summary>
    internal void BeginDeformer(IPreparableLandscapeDeformer deformer) => _current = deformer;

    /// <summary>The chunks of an image this deformer will sample. Unioned per image across the whole
    /// build, so many placements of one image cost one query and one copy.</summary>
    public void ImageChunks(PaintImage image, ImageChunkRect rect)
    {
        if (!_imageChunks.TryGetValue(image, out HashSet<ImageChunkCoord>? coords))
        {
            coords = [];
            _imageChunks[image] = coords;
        }

        foreach (ImageChunkCoord coord in rect.Coords())
        {
            coords.Add(coord);
        }
    }

    /// <summary>Says this deformer's <see cref="IPreparableLandscapeDeformer.Prepare"/> must run on the
    /// main thread. Every deformer that asks is drained in one hop, not one hop each.</summary>
    public void RequiresMainThread()
    {
        if (_current is { } deformer)
        {
            _mainThread.Add(deformer);
        }
    }
}

/// <summary>What the preparation loaded, handed back to each deformer that asked for it.</summary>
public sealed class LandscapeBuildResources
{
    private readonly IReadOnlyDictionary<PaintImage, ImageChunkTable> _imageChunks;

    internal LandscapeBuildResources(IReadOnlyDictionary<PaintImage, ImageChunkTable> imageChunks) =>
        _imageChunks = imageChunks;

    /// <summary>The chunks loaded for this image, or <see cref="ImageChunkTable.Empty"/> when the image
    /// has nothing stored under the region.</summary>
    public ImageChunkTable ImageChunks(PaintImage image) =>
        _imageChunks.TryGetValue(image, out ImageChunkTable? table) ? table : ImageChunkTable.Empty;
}

/// <summary>
/// A deformer that cannot rasterize from its own persisted fields alone — it needs state the live
/// editor puts there while it is streamed in (resident image pixels, a published procedural build).
/// Offline builds hydrate these before building; the live path never calls this, because streaming
/// already did the same job.
///
/// The instance being prepared came out of a storage scan and is owned by the build that scanned it,
/// so it may keep whatever it is handed — nothing else can see it.
/// </summary>
public interface IPreparableLandscapeDeformer
{
    /// <summary>Declares what this deformer needs. Pure and cheap: called for every scanned deformer,
    /// including ones the builder will later discard.</summary>
    void Request(LandscapeBuildRequest request);

    /// <summary>Takes what was loaded.</summary>
    void Prepare(LandscapeBuildResources resources);
}
