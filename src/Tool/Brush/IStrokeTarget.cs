using Godot;

namespace WorldMapStudio;

/// <summary>What a <see cref="BrushStroke"/> paints onto. The target owns its before-snapshots and builds
/// its own undo command.</summary>
public interface IStrokeTarget
{
    /// <summary>Whether a dab centred here can touch this target.</summary>
    bool Covers(Vector3 world);

    /// <summary>Starts a stroke. False when the target cannot be edited right now.</summary>
    bool Begin();

    /// <summary>Applies one dab. True when anything changed.</summary>
    bool Dab(in BrushDab dab);

    /// <summary>Ends the stroke. The undo command for what it changed, or null when nothing did.</summary>
    IEditCommand? Finish();

    /// <summary>Ends the stroke without a command. Edits already applied stay applied.</summary>
    void Cancel();
}
