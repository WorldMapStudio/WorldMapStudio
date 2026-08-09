namespace WorldMapStudio;

/// <summary>Controls the active edit session, exposed to JS as <c>wms.session</c>.</summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class SessionScriptApi : IScriptModule
{
    private readonly EditSessionManager _sessions;

    public string Name => "session";

    public float Priority => 0f;

    public SessionScriptApi(ScriptingSystem system)
    {
        _sessions = system.Context.EditSessions;
    }

    /// <summary>Whether the active session has any pinned (uncommitted) edits.</summary>
    [ScriptProperty]
    public bool IsDirty => _sessions.Active.IsDirty;

    /// <summary>Keeps every edit in the active session and starts a fresh one — the flush-to-database path.</summary>
    [ScriptFunction]
    public void Commit() => _sessions.Commit();

    /// <summary>Reverts every edit in the active session and starts a fresh one.</summary>
    [ScriptFunction]
    public void Abort() => _sessions.Abort();

    [ScriptFunction]
    public void Undo() => _sessions.Undo();

    [ScriptFunction]
    public void Redo() => _sessions.Redo();
}
