namespace WorldMapStudio;

/// <summary>
/// Where a committed <see cref="EditSession"/>'s entities are persisted. Implemented by
/// <see cref="DatabaseSystem"/> and bound into <see cref="EditSessionManager"/> by
/// <see cref="EditorContext"/>.
///
/// This exists so that persisting is not a second call every caller has to remember: committing used
/// to mean "write through the database, <em>then</em> clear the session", which the menu did and the
/// scripting API did not — so every edit made from a script was discarded instead of saved. There is
/// now exactly one <see cref="EditSessionManager.Commit"/>, and it does both halves.
/// </summary>
public interface IEditSessionStore
{
    /// <summary>Writes everything the session pinned. Called before the session is cleared.</summary>
    void Persist(EditSession session);
}
