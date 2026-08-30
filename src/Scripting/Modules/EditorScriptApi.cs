using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>World reload/lifecycle controls, exposed to JS as <c>wms.editor</c>.</summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class EditorScriptApi : IScriptModule
{
    // How often Reload() checks whether the world has finished coming back, while it awaits.
    private const int PollMilliseconds = 20;

    private readonly EditorContext _context;

    public string Name => "editor";

    public float Priority => 0f;

    public EditorScriptApi(ScriptingSystem system)
    {
        _context = system.Context;
    }

    /// <summary>Whether background work is still in flight — a reload would have to wait for this.</summary>
    [ScriptProperty]
    public bool IsBusy => _context.Lifecycle.IsBusy;

    /// <summary>Why an exclusive world operation cannot start right now, or null when one can.</summary>
    [ScriptProperty]
    public string? Blocker => _context.Operations.Blocker;

    /// <summary>
    /// Drops every loaded entity and catalog and reads them back fresh from the database. Aborts the
    /// active edit session first if it is dirty (see <see cref="EditSessionManager.Abort"/>) — nothing
    /// prompts for confirmation here, unlike the "Reload World" menu item, since a script asking for
    /// this has already decided. The returned <see cref="Task"/> completes once the reload lands, not
    /// merely once it starts.
    /// </summary>
    [ScriptFunction]
    public async Task Reload()
    {
        _context.RequestReload("Script requested reload");

        while (_context.IsReloading || _context.PendingReloadReason != null)
        {
            await Task.Delay(PollMilliseconds).ConfigureAwait(false);
        }
    }
}
