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

        // Both fingerprints come from the recorded edit bytes, which this already holds, rather than
        // from rolling the image back to its "before" content and reading it there. A landscape build
        // worker samples this image's chunks while it rebuilds, so that round trip was a structural
        // mutation of the live chunk table under a concurrent reader: a stroke's terrain would land at
        // its pre-stroke height for a frame on release, and worse was available.
        Dictionary<SceneEntity, ChunkChangeSnapshot> beforeSnapshots =
            CaptureAll(affected, coords, ConcatenateChunkBytes(edits, before: true));
        Dictionary<SceneEntity, ChunkChangeSnapshot> afterSnapshots =
            CaptureAll(affected, coords, ConcatenateChunkBytes(edits, before: false));

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
    private static Dictionary<SceneEntity, ChunkChangeSnapshot> CaptureAll(
        IEnumerable<SceneEntity> entities,
        IReadOnlyCollection<ImageChunkCoord> coords,
        byte[] contentHash)
    {
        var result = new Dictionary<SceneEntity, ChunkChangeSnapshot>();

        foreach (SceneEntity entity in entities)
        {
            if (entity.Component<ImageComponent>() is not { } component ||
                component.WorldBoundsForChunks(coords) is not { } bounds)
            {
                continue;
            }

            string fingerprint = ChunkChangeSnapshot.FingerprintBytes(
                entity, contentHash, entity.Map.Value, component.ImageId, component.WorldSizeX, component.WorldSizeZ);
            result[entity] = new ChunkChangeSnapshot(entity.Map, bounds, fingerprint);
        }

        return result;
    }

    // One side of the recorded edits, in a deterministic order so the same content always hashes the
    // same way regardless of paint order. A chunk with no bytes on that side did not exist then, which
    // is what reading an absent chunk off the image used to report.
    private static byte[] ConcatenateChunkBytes(
        IReadOnlyList<(ImageChunkCoord Coord, byte[]? Before, byte[]? After)> edits,
        bool before)
    {
        using var stream = new MemoryStream();
        foreach ((_, byte[]? beforeBytes, byte[]? afterBytes) in
            edits.OrderBy(edit => edit.Coord.Y).ThenBy(edit => edit.Coord.X))
        {
            if ((before ? beforeBytes : afterBytes) is { } bytes)
            {
                stream.Write(bytes);
            }
        }

        return stream.ToArray();
    }
}
