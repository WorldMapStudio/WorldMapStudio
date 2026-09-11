using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Collects every <see cref="IWorldParticipant"/> in the editor and drives them, in both directions,
/// as one ordered list — replacing what used to be a hardcoded load sequence
/// (<see cref="EditorContext.LoadContent"/>) with no unload at all.
///
/// Participants come from two places: the core spine members <see cref="EditorContext"/> constructs
/// directly (they are not <see cref="ISubsystem"/>s, so nothing else would ever find them), and a
/// recursive walk of the subsystem tree — <c>EditorContext → MenuBarManager → WindowManager →
/// Window</c>, <c>DatabaseSystem → Storage</c>, and so on — which is what reaches every window and
/// every plugin without naming any of them individually.
/// </summary>
public sealed class WorldLifecycle
{
    private readonly EditorContext _context;

    public WorldLifecycle(EditorContext context)
    {
        _context = context;
    }

    /// <summary>Bumps every time <see cref="Unload"/> completes. Cheap staleness check for anything
    /// that caches across a reload.</summary>
    public int Generation { get; private set; }

    /// <summary>Every known participant, deduplicated. Recomputed each call: participants come and go
    /// as windows open/close their own hosted subsystems, and this is never called from a hot path.</summary>
    public IReadOnlyList<IWorldParticipant> Participants => Collect(_context);

    /// <summary>
    /// Whether a participant is still doing background work that writes into state <see cref="Unload"/>
    /// is about to drop — a streaming scan landing, an image chunk load completing, a landscape rebuild
    /// applying. A reload waits for this to go false before unloading.
    ///
    /// Deliberately narrower than "anything on <see cref="WorkQueue"/> is active": that includes
    /// texture loads, model loads, and asset indexing, which run almost continuously while flying
    /// around the viewport but write into caches this reload never touches. Gating on those made every
    /// reload sit out most of the quiesce timeout for work with nothing to do with the world being
    /// dropped — see the "Reload World" report this fixed.
    /// </summary>
    public bool IsBusy => Participants.Any(participant => participant.IsBusy);

    /// <summary>Reads every participant's state from the database, in ascending <see cref="IWorldParticipant.LoadPriority"/>
    /// order. May run off the main thread — see <see cref="EditorContext.LoadContent"/>, which this replaces.</summary>
    public void Load(Action<string>? onStep = null) => RunLoad(Participants, onStep);

    /// <summary>
    /// Reverts a still-dirty session first — its pins are the only thing keeping uncommitted edits
    /// alive in the scene/catalog registries, and no participant's <see cref="IWorldParticipant.UnloadWorld"/>
    /// checks for a pin before it clears its own registry. Doing this after teardown (which is where it
    /// used to live, inside <see cref="Verify"/>) was too late: by then the pinned entities were already
    /// gone, so aborting found nothing left to revert and a create-then-reload sequence lost work
    /// silently instead of behaving like the abort it actually was. Every reload path funnels through
    /// here for exactly this reason — see <see cref="EditorScriptApi.Reload"/>, which documents this
    /// exact guarantee and, before this fix, did not actually get it.
    /// </summary>
    private void RevertDirtySession()
    {
        if (_context.EditSessions.Active.IsDirty)
        {
            GD.PushWarning("[World] Reverting dirty edit session before unload.");
            _context.EditSessions.AbortInMemory();
        }
    }

    /// <summary>
    /// Drops every participant's state, in exact reverse of <see cref="Load"/>'s order, then verifies
    /// the core registries actually ended up empty. Returns a description of anything that had to be
    /// force-cleared (a participant that forgot its half of the contract) — empty when everything
    /// unloaded itself cleanly.
    /// </summary>
    public IReadOnlyList<string> Unload()
    {
        RevertDirtySession();

        List<string> report = RunUnload(Participants);
        report.AddRange(Verify());
        Generation++;
        return report;
    }

    /// <summary>The most recent <see cref="RunLoad"/>'s phase breakdown — a load happens on startup and
    /// on every world reload, neither of which has a log window open by default, so this is what the
    /// Performance window's "Last Load" tab reads instead of asking someone to go find it in the log.</summary>
    public static LoadReport? LastLoad { get; private set; }

    public readonly record struct LoadReport(DateTime CompletedUtc, double WallSeconds, IReadOnlyList<string> Lines);

    /// <summary>The load half, factored out so it can be driven against a fake participant list in
    /// tests without a live <see cref="EditorContext"/> behind it.</summary>
    internal static void RunLoad(IEnumerable<IWorldParticipant> participants, Action<string>? onStep)
    {
        var timings = new PhaseTimings();
        foreach (IWorldParticipant participant in participants.OrderBy(participant => participant.LoadPriority))
        {
            if (participant.LoadStep is { } step)
            {
                onStep?.Invoke(step);
            }

            using (timings.Measure(participant.LoadStep ?? participant.GetType().Name))
            {
                participant.LoadWorld(timings);
            }
        }

        // A load that follows a bulk import can spend minutes in one participant reading back what the
        // import wrote, with nothing on screen to say which. Printed rather than logged behind
        // DiagnosticLog.Enabled: a load is rare, and the answer is wanted on the run that surprised
        // someone, not on the one after they went back and turned logging on.
        foreach (string line in timings.Format("World load", minimumSeconds: 0.1))
        {
            GD.Print($"[World] {line}");
            DiagnosticLog.Log(line);
        }

        // Unthresholded, unlike the log above: a UI tab someone opens on purpose isn't spam, and a
        // catalog type too small to earn a log line is exactly the kind of thing worth ruling out there.
        LastLoad = new LoadReport(DateTime.UtcNow, timings.ElapsedSeconds, timings.Format("World load"));
    }

    /// <summary>
    /// The unload half. Each participant runs inside its own try/catch: one broken participant must
    /// not leave the rest of the world loaded, since that is worse than the partial unload it was
    /// trying to avoid. Failures come back as messages rather than being swallowed, alongside a
    /// <c>GD.PushError</c> naming the participant.
    /// </summary>
    internal static List<string> RunUnload(IEnumerable<IWorldParticipant> participants)
    {
        var errors = new List<string>();
        foreach (IWorldParticipant participant in participants.OrderByDescending(participant => participant.LoadPriority))
        {
            try
            {
                participant.UnloadWorld();
            }
            catch (Exception e)
            {
                string message = $"{participant.GetType().Name} failed to unload: {e.Message}";
                errors.Add(message);
                GD.PushError($"[World] {message}");
            }
        }

        return errors;
    }

    // The safety net: whatever a participant forgot, this is what stops it surviving a reload
    // silently. Force-clears the core registries and reports what it found, so a missing
    // UnloadWorld shows up immediately instead of as a stale entity nobody can explain later.
    private List<string> Verify()
    {
        var leftover = new List<string>();

        if (_context.Scene.Entities.Count > 0)
        {
            foreach (SceneEntity entity in _context.Scene.Entities)
            {
                leftover.Add($"scene entity '{entity.DisplayName}' ({entity.GetType().Name})");
                entity.DestroyRepresentation();
            }

            _context.Scene.Clear();
        }

        if (_context.Catalog.Entities.Count > 0)
        {
            foreach (CatalogEntity entity in _context.Catalog.Entities)
            {
                leftover.Add($"catalog entity '{entity.DisplayName}' ({entity.GetType().Name})");
            }

            _context.Catalog.Clear();
        }

        if (_context.Selection.Selected.Count > 0)
        {
            leftover.Add($"{_context.Selection.Selected.Count} selected entity/entities");
            _context.Selection.Clear();
        }

        if (_context.Clipboard.HasContent)
        {
            leftover.Add("clipboard contents");
            _context.Clipboard.Clear();
        }

        if (_context.Problems.Count > 0)
        {
            leftover.Add($"{_context.Problems.Count} reported problem(s)");
            _context.Problems.Clear();
        }

        if (_context.EditSessions.Active.IsDirty)
        {
            leftover.Add("dirty edit session");
            _context.EditSessions.AbortInMemory();
        }

        foreach (string entry in leftover)
        {
            GD.PushError($"[World] Leftover after unload, force-cleared: {entry}");
        }

        return leftover;
    }

    private static List<IWorldParticipant> Collect(EditorContext context)
    {
        var seen = new HashSet<IWorldParticipant>(ReferenceEqualityComparer.Instance);
        var result = new List<IWorldParticipant>();

        void Visit(object? node)
        {
            if (node is IWorldParticipant participant && seen.Add(participant))
            {
                result.Add(participant);
            }

            if (node is ISubsystemHost host)
            {
                foreach (ISubsystem subsystem in host.Subsystems)
                {
                    Visit(subsystem);
                }
            }
        }

        // Core spine: constructed directly by EditorContext, so nothing self-registers these —
        // they have to be named once, here, rather than discovered.
        Visit(context.Selection);
        Visit(context.Clipboard);
        Visit(context.EditSessions);
        Visit(context.Scene);
        Visit(context.Catalog);
        Visit(context.Problems);
        Visit(context.Tools);
        Visit(context.Database);
        Visit(context.Maps);
        Visit(context.ModelFormats);
        Visit(context.Assets);
        Visit(context.MeshMaterials);
        Visit(context.Landscape);
        Visit(context.Procedural);
        Visit(context.Images);
        Visit(context.Prefabs);
        Visit(context.Streaming);
        Visit(context.Environments);
        Visit(context.Batch);
        Visit(context.ComponentTypes);
        Visit(context.MapProperties);
        Visit(context.Scripting);
        Visit(context.Migrations);

        // Recurses into every self-registered subsystem: MenuBarManager -> WindowManager -> each
        // Window, and any plugin type that hangs directly off EditorContext the same way.
        Visit(context);

        return result;
    }
}
