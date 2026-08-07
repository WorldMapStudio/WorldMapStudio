namespace WorldMapStudio;

/// <summary>
/// Owns the editor's active <see cref="EditSession"/>. Committing or aborting the current session
/// starts a fresh empty one, so there is always a session to record edits into.
/// </summary>
public sealed class EditSessionManager
{
    public EditSession Active { get; private set; } = new();

    public void Record(IEditCommand command) => Active.Record(command);

    public void Undo() => Active.History.Undo();

    public void Redo() => Active.History.Redo();

    public void Commit()
    {
        Active.Commit();
        Active = new EditSession();
    }

    public void Abort()
    {
        Active.Abort();
        Active = new EditSession();
    }
}
