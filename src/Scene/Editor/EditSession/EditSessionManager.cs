using System;

namespace WorldMapStudio;

/// <summary>
/// Owns the editor's active <see cref="EditSession"/>. Committing or aborting the current session
/// starts a fresh empty one, so there is always a session to record edits into.
///
/// <see cref="Commit"/> persists through the bound <see cref="IEditSessionStore"/> before clearing.
/// That is deliberately not the caller's job: when it was, the menu remembered to write through the
/// database and the scripting API did not, so script edits were silently dropped.
///
/// <see cref="Abort"/> is a revert *and* a full world reload, not the revert alone. The in-memory
/// undo only covers what the session recorded through <see cref="IEditCommand"/>s; it says nothing
/// about system or non-scene entities another part of the editor may have changed outside the
/// session's bookkeeping. A reload is the actual guarantee that abandoning a session leaves the
/// editor exactly where opening the project fresh would have. See <see cref="AbortInMemory"/> for
/// the revert alone, which is what this used to be and is still what tests exercise directly.
/// </summary>
public sealed class EditSessionManager : IWorldParticipant
{
    private IEditSessionStore? _store;
    private StreamingSystem? _streaming;
    private Action? _requestReload;
    private Func<string?>? _activeOperationCheck;

    public EditSession Active { get; private set; } = new();

    /// <summary>
    /// Binds where committed sessions are written. Called once by <see cref="EditorContext"/>, after
    /// the database exists. Left unbound (in tests, say) a commit simply keeps the edits in memory.
    /// </summary>
    public void BindStore(IEditSessionStore store) => _store = store;

    /// <summary>
    /// Binds the streaming system so releasing a session's pins can re-judge what stays loaded. Left
    /// unbound (in tests, say) a commit or abort simply leaves the loaded set untouched.
    /// </summary>
    public void BindStreaming(StreamingSystem streaming) => _streaming = streaming;

    /// <summary>
    /// Binds what <see cref="Abort"/> asks for once it has reverted in memory. Left unbound (in
    /// tests, say) an abort is <see cref="AbortInMemory"/> alone — which is exactly what the existing
    /// unit tests exercise, so nothing about them changes.
    /// </summary>
    public void BindReload(Action requestReload) => _requestReload = requestReload;

    /// <summary>
    /// Binds the gate exclusive world operations run behind, so <see cref="Record"/> can refuse an
    /// edit for the duration of one. Left unbound (in tests, say) recording is never refused.
    /// </summary>
    public void BindOperations(WorldOperations operations) => _activeOperationCheck = () => operations.ActiveOperation;

    /// <summary>Test seam for the same check <see cref="BindOperations"/> wires in production, without
    /// needing a live <see cref="WorldOperations"/> (which needs a live <see cref="EditorContext"/>
    /// behind it) just to exercise <see cref="Record"/>'s guard.</summary>
    internal void BindActiveOperationCheck(Func<string?> activeOperation) => _activeOperationCheck = activeOperation;

    /// <exception cref="InvalidOperationException">An exclusive world operation (see
    /// <see cref="WorldOperations"/>) is currently running. Loud on purpose, the same way recording a
    /// derived-entity target already is: a tool or a script sneaking an edit into a world a batch
    /// operation is rewriting underneath it is a correctness bug, not something to silently drop.</exception>
    public void Record(IEditCommand command)
    {
        if (_activeOperationCheck?.Invoke() is { } operation)
        {
            throw new InvalidOperationException(
                $"Cannot record an edit while exclusive operation '{operation}' is running.");
        }

        Active.Record(command);
    }

    public void Undo() => Active.History.Undo();

    public void Redo() => Active.History.Redo();

    /// <summary>Persists everything the session touched, then clears it and starts a fresh one.</summary>
    public void Commit()
    {
        _store?.Persist(Active);
        Active.Commit();
        Active = new EditSession();

        // Entities that stayed loaded only because this session pinned them are now free to unload.
        // Judged here and now rather than at the next scan: scans are asynchronous and gated on the
        // focus moving, so waiting for one leaves settled entities lingering in the scene.
        _streaming?.Resweep();
    }

    /// <summary>Reverts every recorded edit, then requests a full world reload. See the class docs
    /// for why a revert alone is not enough.</summary>
    public void Abort()
    {
        AbortInMemory();
        _requestReload?.Invoke();
    }

    /// <summary>The in-memory revert alone, with no reload — what <see cref="Abort"/> used to be.
    /// Used by tests, and by the reload itself (which must not recurse into requesting another one).</summary>
    public void AbortInMemory()
    {
        Active.Abort();
        Active = new EditSession();
        _streaming?.Resweep();
    }

    /// <summary>A reload's own unload step: the in-memory revert alone, never the reload request —
    /// the reload already in progress is what called this.</summary>
    public void UnloadWorld() => AbortInMemory();
}
