using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>Replaces a drawing target's raster after a completed paint stroke.</summary>
public sealed class SetDrawingTargetPixelsCommand(
    DrawingTargetComponent target,
    byte[] before,
    byte[] after) : IEditCommand, IChunkChangeCommand
{
    private SceneEntity Entity => target.Owner ?? throw new System.InvalidOperationException("Drawing target component is not attached.");

    public IReadOnlyList<IEntity> Targets { get; } = new IEntity[] { target.Owner! };

    public IReadOnlyList<ChunkChangeImpact> ChunkImpacts { get; } =
    [
        new ChunkChangeImpact(
            target.Owner!,
            ChunkChangeSnapshot.Capture(target.Owner!, fingerprint: ChunkChangeSnapshot.FingerprintBytes(
                target.Owner!, before, target.Width, target.Height, target.WorldSizeX, target.WorldSizeZ,
                target.Strength, target.Channel, target.LayerId, target.MaterialId, target.Priority)),
            ChunkChangeSnapshot.Capture(target.Owner!, fingerprint: ChunkChangeSnapshot.FingerprintBytes(
                target.Owner!, after, target.Width, target.Height, target.WorldSizeX, target.WorldSizeZ,
                target.Strength, target.Channel, target.LayerId, target.MaterialId, target.Priority)))
    ];

    public string Description => $"Paint {Entity.DisplayName}";

    public void Apply() => target.ReplacePixels(after);

    public void Revert() => target.ReplacePixels(before);
}

/// <summary>Changes a drawing target's raster resolution, preserving or restoring its pixel data.</summary>
public sealed class ResizeDrawingTargetCommand(
    DrawingTargetComponent target,
    int beforeWidth,
    int beforeHeight,
    byte[] beforePixels,
    int afterWidth,
    int afterHeight,
    byte[] afterPixels) : IEditCommand, IChunkChangeCommand
{
    private SceneEntity Entity => target.Owner ?? throw new System.InvalidOperationException("Drawing target component is not attached.");

    public IReadOnlyList<IEntity> Targets { get; } = new IEntity[] { target.Owner! };

    public IReadOnlyList<ChunkChangeImpact> ChunkImpacts { get; } =
    [
        new ChunkChangeImpact(
            target.Owner!,
            ChunkChangeSnapshot.Capture(target.Owner!, fingerprint: ChunkChangeSnapshot.FingerprintBytes(
                target.Owner!, beforePixels, beforeWidth, beforeHeight, target.WorldSizeX, target.WorldSizeZ,
                target.Strength, target.Channel, target.LayerId, target.MaterialId, target.Priority)),
            ChunkChangeSnapshot.Capture(target.Owner!, fingerprint: ChunkChangeSnapshot.FingerprintBytes(
                target.Owner!, afterPixels, afterWidth, afterHeight, target.WorldSizeX, target.WorldSizeZ,
                target.Strength, target.Channel, target.LayerId, target.MaterialId, target.Priority)))
    ];

    public string Description => $"Resize {Entity.DisplayName}";

    public void Apply()
    {
        target.LoadPixels(afterWidth, afterHeight, afterPixels);
    }

    public void Revert()
    {
        target.LoadPixels(beforeWidth, beforeHeight, beforePixels);
    }
}
