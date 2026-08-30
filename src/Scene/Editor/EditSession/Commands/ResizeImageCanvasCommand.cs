using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>Changes an image's canvas dimensions via <see cref="PaintImage.ResizeCanvas"/> — a crop or
/// extend, never a resample — and records undo for it. Chunk-based, so this stays safe to use on an
/// image of any size: only the (typically few) chunks a shrink actually drops get snapshotted, never a
/// whole-canvas buffer.
///
/// A shrink is not necessarily reversible in full: <see cref="PaintImage.ResizeCanvas"/> only reports
/// (and this only restores) chunks that were <em>resident</em> at resize time. A stored-but-not-resident
/// chunk outside the new bounds is left alone in memory and in storage — orphaned rather than deleted,
/// which is the safer of the two failure modes when a canvas is too large to ever be fully resident.</summary>
public sealed class ResizeImageCanvasCommand : IEditCommand, IChunkChangeCommand
{
    private readonly PaintImage _image;
    private readonly int _beforeWidth;
    private readonly int _beforeHeight;
    private readonly int _afterWidth;
    private readonly int _afterHeight;
    private readonly IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)> _dropped;

    public ResizeImageCanvasCommand(PaintImage image, int width, int height, IEnumerable<SceneEntity> affectedEntities, string description)
    {
        _image = image;
        _beforeWidth = image.Width;
        _beforeHeight = image.Height;
        Description = description;
        Targets = new IEntity[] { image };

        List<SceneEntity> affected = affectedEntities.ToList();
        Dictionary<SceneEntity, ChunkChangeSnapshot> beforeSnapshots = CaptureAll(affected);

        _dropped = image.ResizeCanvas(width, height);
        _afterWidth = image.Width;
        _afterHeight = image.Height;

        Dictionary<SceneEntity, ChunkChangeSnapshot> afterSnapshots = CaptureAll(affected);
        ChunkImpacts = afterSnapshots
            .Select(pair => new ChunkChangeImpact(pair.Key, beforeSnapshots.GetValueOrDefault(pair.Key), pair.Value))
            .ToList();
    }

    public IReadOnlyList<IEntity> Targets { get; }

    public IReadOnlyList<ChunkChangeImpact> ChunkImpacts { get; }

    public string Description { get; }

    public void Apply() => _image.ResizeCanvas(_afterWidth, _afterHeight);

    public void Revert()
    {
        _image.ResizeCanvas(_beforeWidth, _beforeHeight);
        _image.ApplyChunkEdits(_dropped.Select(entry => (entry.Coord, (byte[]?)entry.Pixels)));
    }

    // Resize changes the canvas as a whole, unlike a paint stroke — the whole placement's footprint is
    // the right export bound here, not just the dropped chunks' corner.
    private static Dictionary<SceneEntity, ChunkChangeSnapshot> CaptureAll(IEnumerable<SceneEntity> entities) =>
        entities.ToDictionary(entity => entity, entity => ChunkChangeSnapshot.Capture(entity));
}
