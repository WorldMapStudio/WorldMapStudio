using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>Replaces a drawing target's raster after a completed paint stroke.</summary>
public sealed class SetDrawingTargetPixelsCommand(
    DrawingTargetEntity target,
    byte[] before,
    byte[] after) : IEditCommand, IChunkChangeCommand
{
    public IReadOnlyList<IEntity> Targets { get; } = new IEntity[] { target };

    public IReadOnlyList<ChunkChangeImpact> ChunkImpacts { get; } =
    [
        new ChunkChangeImpact(
            target,
            ChunkChangeSnapshot.Capture(target, fingerprint: ChunkChangeSnapshot.FingerprintBytes(
                target, before, target.Width, target.Height, target.WorldSizeX, target.WorldSizeZ,
                target.Strength, target.Channel, target.LayerId, target.MaterialId, target.Priority)),
            ChunkChangeSnapshot.Capture(target, fingerprint: ChunkChangeSnapshot.FingerprintBytes(
                target, after, target.Width, target.Height, target.WorldSizeX, target.WorldSizeZ,
                target.Strength, target.Channel, target.LayerId, target.MaterialId, target.Priority)))
    ];

    public string Description => $"Paint {target.DisplayName}";

    public void Apply() => target.ReplacePixels(after);

    public void Revert() => target.ReplacePixels(before);
}

/// <summary>Changes a drawing target's raster resolution, preserving or restoring its pixel data.</summary>
public sealed class ResizeDrawingTargetCommand(
    DrawingTargetEntity target,
    int beforeWidth,
    int beforeHeight,
    byte[] beforePixels,
    int afterWidth,
    int afterHeight,
    byte[] afterPixels) : IEditCommand, IChunkChangeCommand
{
    public IReadOnlyList<IEntity> Targets { get; } = new IEntity[] { target };

    public IReadOnlyList<ChunkChangeImpact> ChunkImpacts { get; } =
    [
        new ChunkChangeImpact(
            target,
            ChunkChangeSnapshot.Capture(target, fingerprint: ChunkChangeSnapshot.FingerprintBytes(
                target, beforePixels, beforeWidth, beforeHeight, target.WorldSizeX, target.WorldSizeZ,
                target.Strength, target.Channel, target.LayerId, target.MaterialId, target.Priority)),
            ChunkChangeSnapshot.Capture(target, fingerprint: ChunkChangeSnapshot.FingerprintBytes(
                target, afterPixels, afterWidth, afterHeight, target.WorldSizeX, target.WorldSizeZ,
                target.Strength, target.Channel, target.LayerId, target.MaterialId, target.Priority)))
    ];

    public string Description => $"Resize {target.DisplayName}";

    public void Apply()
    {
        target.LoadPixels(afterWidth, afterHeight, afterPixels);
    }

    public void Revert()
    {
        target.LoadPixels(beforeWidth, beforeHeight, beforePixels);
    }
}
