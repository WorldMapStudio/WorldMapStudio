namespace WorldMapStudio;

/// <summary>
/// Something with per-frame work. Implemented directly by the core spine members of
/// <see cref="EditorContext"/>, and by any subsystem (a window, a plugin cache) that needs the same
/// seam — <see cref="FrameLoop"/> finds those by walking the subsystem tree.
/// </summary>
public interface IFrameParticipant
{
    /// <summary>Tick order. Only matters between participants whose tick depends on another's having
    /// already run this frame; ties are fine for everything else.</summary>
    float TickPriority => 0f;

    void Update();
}
