using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>Replaces a drawing target's raster after a completed paint stroke.</summary>
public sealed class SetDrawingTargetPixelsCommand(
    DrawingTargetEntity target,
    byte[] before,
    byte[] after) : IEditCommand
{
    public IReadOnlyList<IEntity> Targets { get; } = new IEntity[] { target };

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
    byte[] afterPixels) : IEditCommand
{
    public IReadOnlyList<IEntity> Targets { get; } = new IEntity[] { target };

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
