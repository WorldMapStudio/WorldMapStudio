using System;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// The gate exclusive, world-rewriting operations run behind — a batch import, a migration, a future
/// ADT-style bulk conversion. These are not permitted to run while a session is editing, and ordinary
/// editing is not permitted to run while one of them is: <see cref="EditSessionManager.Record"/>
/// throws for the duration, the same way it already throws for a derived-entity target.
///
/// A batch operation writes to the database directly, behind the loaded world's back, so the loaded
/// world is stale by definition once it finishes — <see cref="TryRun"/> defaults to reloading
/// afterward for exactly that reason.
/// </summary>
public sealed class WorldOperations
{
    private readonly EditorContext _context;

    private volatile string? _activeOperation;

    public WorldOperations(EditorContext context)
    {
        _context = context;
    }

    /// <summary>Non-null while an exclusive operation owns the world.</summary>
    public string? ActiveOperation => _activeOperation;

    /// <summary>Why an exclusive operation cannot start right now, or null when one can.</summary>
    public string? Blocker
    {
        get
        {
            if (_activeOperation is { } running)
            {
                return $"'{running}' is already running.";
            }

            if (_context.PendingReloadReason != null)
            {
                return "The world is reloading.";
            }

            if (_context.EditSessions.Active.IsDirty)
            {
                return "Commit or abort the edit session first.";
            }

            return null;
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> on <see cref="WorkQueue"/> with exclusive ownership of the world:
    /// no session edit can be recorded (<see cref="EditSessionManager.Record"/> throws) and no other
    /// exclusive operation can start until it finishes. Returns null with <paramref name="blocker"/>
    /// set, and does not schedule anything, if the world is not currently free to take one — see
    /// <see cref="Blocker"/>.
    /// </summary>
    /// <param name="reloadAfter">Whether to request a world reload once <paramref name="work"/>
    /// completes (successfully or not). Defaults on: an operation that earns this gate is, by
    /// definition, one that just rewrote what the loaded world was showing. A caller whose work
    /// doesn't touch the loaded world's own content (e.g. an export, which only reads committed state
    /// and writes elsewhere) should pass false — there's nothing to reload.</param>
    public WorkHandle? TryRun(string name, Func<WorkContext, Task> work, out string? blocker, bool reloadAfter = true)
    {
        blocker = Blocker;
        if (blocker != null)
        {
            return null;
        }

        _activeOperation = name;
        return WorkQueue.Schedule(name, async ctx =>
        {
            // A plain try/finally, not a catch: WorkQueue's own runner already logs and records a
            // fault for whatever work(ctx) throws. This only needs to run regardless of the outcome.
            try
            {
                await work(ctx).ConfigureAwait(false);
            }
            finally
            {
                _activeOperation = null;
                if (reloadAfter)
                {
                    _context.RequestReload($"'{name}' finished");
                }
            }
        });
    }
}
