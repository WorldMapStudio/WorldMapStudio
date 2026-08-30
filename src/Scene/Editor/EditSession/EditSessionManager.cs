namespace WorldMapStudio;

/// <summary>
/// Owns the editor's active <see cref="EditSession"/>. Committing or aborting the current session
/// starts a fresh empty one, so there is always a session to record edits into.
///
/// <see cref="Commit"/> persists through the bound <see cref="IEditSessionStore"/> before clearing.
/// That is deliberately not the caller's job: when it was, the menu remembered to write through the
/// database and the scripting API did not, so script edits were silently dropped.
/// </summary>
public sealed class EditSessionManager
{
    private IEditSessionStore? _store;
    private StreamingSystem? _streaming;

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

    public void Record(IEditCommand command) => Active.Record(command);

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

    public void Abort()
    {
        Active.Abort();
        Active = new EditSession();
        _streaming?.Resweep();
    }
}
