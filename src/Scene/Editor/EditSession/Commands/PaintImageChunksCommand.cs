using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WorldMapStudio;

/// <summary>Replaces a set of an image's chunks with recorded before/after content — the per-chunk
/// undo shape a completed paint stroke (or a chunk-based clear, via <see cref="PaintImage.ClearAll"/>)
/// needs. A stroke typically touches a handful of chunks, so this only snapshots those rather than the
/// whole canvas.
///
/// Snapshots every entity in <paramref name="affectedEntities"/>, not just the placement the tool
/// happened to be pointed at: an image may be shared by several placements, so every placement's chunk
/// fingerprint can move even though only one of them was painted. Each entity's
/// <see cref="ChunkChangeSnapshot"/> bounds are narrowed to the world AABB of the touched chunks
/// (<see cref="ImageComponent.WorldBoundsForChunks"/>) rather than that placement's whole footprint, so
/// a small stroke on a huge image does not mark unrelated terrain dirty for export.</summary>
public sealed class PaintImageChunksCommand : IEditCommand, IChunkChangeCommand, ISharedResourceChunkCommand
{
    private readonly PaintImage _image;
    private readonly IReadOnlyList<(ImageChunkCoord Coord, byte[]? Before, byte[]? After)> _edits;

    public PaintImageChunksCommand(
        PaintImage image,
        IReadOnlyList<(ImageChunkCoord Coord, byte[]? Before, byte[]? After)> edits,
        IEnumerable<SceneEntity> affectedEntities,
        string description)
    {
        _image = image;
        _edits = edits;
        Description = description;
        Targets = new IEntity[] { image };

        List<SceneEntity> affected = affectedEntities.ToList();
        List<ImageChunkCoord> coords = edits.Select(edit => edit.Coord).ToList();

        _image.ApplyChunkEdits(edits.Select(edit => (edit.Coord, edit.Before)));
        Dictionary<SceneEntity, ChunkChangeSnapshot> beforeSnapshots = CaptureAll(affected, coords);
        _image.ApplyChunkEdits(edits.Select(edit => (edit.Coord, edit.After)));
        Dictionary<SceneEntity, ChunkChangeSnapshot> afterSnapshots = CaptureAll(affected, coords);

        ChunkImpacts = afterSnapshots
            .Select(pair => new ChunkChangeImpact(pair.Key, beforeSnapshots.GetValueOrDefault(pair.Key), pair.Value))
            .ToList();
    }

    public IReadOnlyList<IEntity> Targets { get; }

    public IReadOnlyList<ChunkChangeImpact> ChunkImpacts { get; }

    public (Type Type, int Id)? SharedResource =>
        _image.RecordId is int id ? (typeof(PaintImage), id) : null;

    public string Description { get; }

    public void Apply() => _image.ApplyChunkEdits(_edits.Select(edit => (edit.Coord, edit.After)));

    public void Revert() => _image.ApplyChunkEdits(_edits.Select(edit => (edit.Coord, edit.Before)));

    // One snapshot per affected placement, bounded to just the touched chunks' world footprint rather
    // than ChunkChangeSnapshot.Capture's whole-entity bounds.
    private Dictionary<SceneEntity, ChunkChangeSnapshot> CaptureAll(IEnumerable<SceneEntity> entities, IReadOnlyCollection<ImageChunkCoord> coords)
    {
        var result = new Dictionary<SceneEntity, ChunkChangeSnapshot>();
        byte[]? contentHash = null;

        foreach (SceneEntity entity in entities)
        {
            if (entity.Component<ImageComponent>() is not { } component ||
                component.WorldBoundsForChunks(coords) is not { } bounds)
            {
                continue;
            }

            contentHash ??= ConcatenateChunkBytes(coords);
            string fingerprint = ChunkChangeSnapshot.FingerprintBytes(
                entity, contentHash, entity.Map.Value, component.ImageId, component.WorldSizeX, component.WorldSizeZ);
            result[entity] = new ChunkChangeSnapshot(entity.Map, bounds, fingerprint);
        }

        return result;
    }

    // Deterministic order so the same content always hashes the same way regardless of paint order.
    private byte[] ConcatenateChunkBytes(IEnumerable<ImageChunkCoord> coords)
    {
        using var stream = new MemoryStream();
        foreach (ImageChunkCoord coord in coords.OrderBy(coord => coord.Y).ThenBy(coord => coord.X))
        {
            if (_image.CopyChunkBytes(coord) is { } bytes)
            {
                stream.Write(bytes);
            }
        }

        return stream.ToArray();
    }
}
