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

    public EditSession Active { get; private set; } = new();

    /// <summary>
    /// Binds where committed sessions are written. Called once by <see cref="EditorContext"/>, after
    /// the database exists. Left unbound (in tests, say) a commit simply keeps the edits in memory.
    /// </summary>
    public void BindStore(IEditSessionStore store) => _store = store;

    public void Record(IEditCommand command) => Active.Record(command);

    public void Undo() => Active.History.Undo();

    public void Redo() => Active.History.Redo();

    /// <summary>Persists everything the session touched, then clears it and starts a fresh one.</summary>
    public void Commit()
    {
        _store?.Persist(Active);
        Active.Commit();
        Active = new EditSession();
    }

    public void Abort()
    {
        Active.Abort();
        Active = new EditSession();
    }
}
