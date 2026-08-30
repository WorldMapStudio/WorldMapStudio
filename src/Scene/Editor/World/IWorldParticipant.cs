namespace WorldMapStudio;

/// <summary>
/// Something that holds state read from (or derived from) the project's database, and can therefore
/// be dropped and re-read. Implemented by the core spine members of <see cref="EditorContext"/>
/// directly, and by any <see cref="ISubsystem"/> (a storage, a window, a plugin catalog) that needs
/// the same seam — <see cref="WorldLifecycle"/> finds those by walking the subsystem tree.
///
/// A participant that implements <see cref="LoadWorld"/> should implement <see cref="UnloadWorld"/>
/// too, and vice versa: one without the other is exactly the "someone forgot to clean up" bug this
/// interface exists to make impossible. <see cref="WorldLifecycle"/>'s post-unload verification pass
/// is the backstop for whichever half got missed.
/// </summary>
public interface IWorldParticipant
{
    /// <summary>Load order. <see cref="WorldLifecycle.Unload"/> runs participants in exact reverse.
    /// Only matters between participants whose load depends on another's having already run (e.g.
    /// landscape settings need the current map); ties are fine for everything else.</summary>
    float LoadPriority => 0f;

    /// <summary>Caption shown on the reload screen while <see cref="LoadWorld"/> runs. Null for
    /// participants with nothing worth narrating (most windows, most core systems).</summary>
    string? LoadStep => null;

    /// <summary>Reads this participant's state from the database. May run off the main thread — see
    /// the implementing type for whether it does.</summary>
    void LoadWorld() { }

    /// <summary>
    /// Drops everything read (or derived) since the last <see cref="LoadWorld"/>. Always runs on the
    /// main thread. Must be idempotent (called on an already-empty participant is a no-op) and must
    /// not throw for "nothing to unload" — <see cref="WorldLifecycle.Unload"/> still calls every
    /// other participant if one of them does throw for a real reason, but a participant that throws
    /// routinely defeats that safety net.
    /// </summary>
    void UnloadWorld() { }

    /// <summary>Whether background work this participant owns is still in flight. A reload waits for
    /// this to go false (with a timeout) before calling <see cref="UnloadWorld"/>, so background work
    /// never completes into state that was just dropped out from under it.</summary>
    bool IsBusy => false;
}
