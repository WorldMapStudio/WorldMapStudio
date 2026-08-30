using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>Replaces a <see cref="PaintImage"/>'s resolution and pixels wholesale — one command for
/// both a completed paint stroke (same resolution, different pixels) and a resize (both changed),
/// since both are "swap the raster" the same way <see cref="SetNetworkCommand"/> is "swap the graph"
/// for every network-editing component.
///
/// Snapshots every entity in <paramref name="affectedEntities"/>, not just the placement the tool
/// happened to be pointed at: an image may be shared by several placements, so every placement's
/// chunk fingerprint can move even though only one of them was painted.</summary>
public sealed class SetImageContentCommand : IEditCommand, IChunkChangeCommand
{
    private readonly PaintImage _image;
    private readonly int _beforeWidth;
    private readonly int _beforeHeight;
    private readonly byte[] _beforePixels;
    private readonly int _afterWidth;
    private readonly int _afterHeight;
    private readonly byte[] _afterPixels;

    public SetImageContentCommand(
        PaintImage image,
        int beforeWidth,
        int beforeHeight,
        byte[] beforePixels,
        int afterWidth,
        int afterHeight,
        byte[] afterPixels,
        IEnumerable<SceneEntity> affectedEntities,
        string description)
    {
        _image = image;
        _beforeWidth = beforeWidth;
        _beforeHeight = beforeHeight;
        _beforePixels = beforePixels;
        _afterWidth = afterWidth;
        _afterHeight = afterHeight;
        _afterPixels = afterPixels;
        Description = description;
        Targets = new IEntity[] { image };

        List<SceneEntity> affected = affectedEntities.ToList();
        image.LoadPixels(beforeWidth, beforeHeight, beforePixels);
        Dictionary<SceneEntity, ChunkChangeSnapshot> beforeSnapshots = CaptureAll(affected);
        image.LoadPixels(afterWidth, afterHeight, afterPixels);
        Dictionary<SceneEntity, ChunkChangeSnapshot> afterSnapshots = CaptureAll(affected);

        ChunkImpacts = afterSnapshots
            .Select(pair => new ChunkChangeImpact(pair.Key, beforeSnapshots.GetValueOrDefault(pair.Key), pair.Value))
            .ToList();
    }

    public IReadOnlyList<IEntity> Targets { get; }

    public IReadOnlyList<ChunkChangeImpact> ChunkImpacts { get; }

    public string Description { get; }

    public void Apply() => _image.LoadPixels(_afterWidth, _afterHeight, _afterPixels);

    public void Revert() => _image.LoadPixels(_beforeWidth, _beforeHeight, _beforePixels);

    private static Dictionary<SceneEntity, ChunkChangeSnapshot> CaptureAll(IEnumerable<SceneEntity> entities) =>
        entities.ToDictionary(entity => entity, entity => ChunkChangeSnapshot.Capture(entity));
}
