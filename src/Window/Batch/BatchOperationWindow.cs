using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// Picks a batch operation, edits its settings, and runs it. Everything about what an operation
/// processes — dirty counts, staleness, what "already done" means — belongs to the operation and is
/// drawn in its own <see cref="IBatchOperation.DrawSettings"/>, not here.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class BatchOperationWindow : Window
{
    public override string? Category => "World";

    // Alt+X still belongs to the export window; this moves onto it once that is gone.
    public override KeyboardShortcut DefaultShortcut => new(ImGuiKey.B, ShortcutModifiers.Alt);

    private const string ProgressPopupId = "Batch Progress";

    private static readonly NVector4 FaultedColor = new(1.0f, 0.45f, 0.40f, 1.0f);
    private static readonly NVector4 CompletedColor = new(0.42f, 0.85f, 0.46f, 1.0f);
    private static readonly NVector4 MutedColor = new(0.60f, 0.60f, 0.60f, 1.0f);

    private readonly BatchSystem _batch;

    private string? _selectedId;
    private string _lastSavedSettingsJson = "";
    private string? _lastSavedSettingsId;

    private BatchRun? _running;
    private bool _popupOpenRequested;

    public BatchOperationWindow(WindowManager manager)
        : base("Batch Operations", startOpen: false, defaultSize: new NVector2(560.0f, 460.0f))
    {
        _batch = manager.Context.Batch;
    }

    /// <summary>Runs even while this window is closed. The progress modal has to stay visible however
    /// the user navigates — editing is blocked for the run's whole duration either way — and the
    /// session banner is the only recovery path for a wedged session, so it must not be reachable
    /// only by finding the right window first.</summary>
    protected override void OnBeforeDraw()
    {
        if (_popupOpenRequested)
        {
            ImGui.OpenPopup(ProgressPopupId);
            _popupOpenRequested = false;
        }

        DrawProgressPopup();
        DrawSessionBanner();
    }

    protected override void DrawContent()
    {
        List<IBatchOperation> operations = _batch.Operations.ToList();
        if (operations.Count == 0)
        {
            ImGui.TextDisabled("No batch operations registered.");
            return;
        }

        bool running = _running is { Work: { } handle } && handle.Snapshot().IsActive;

        if (ImGui.BeginChild("operations", new NVector2(200.0f, 0.0f), true))
        {
            DrawOperationList(operations);
        }

        ImGui.EndChild();
        ImGui.SameLine();

        if (ImGui.BeginChild("selected", NVector2.Zero))
        {
            DrawSelected(operations, running);
        }

        ImGui.EndChild();
    }

    private void DrawOperationList(List<IBatchOperation> operations)
    {
        if (_selectedId == null || operations.All(candidate => candidate.Id != _selectedId))
        {
            _selectedId = operations[0].Id;
        }

        foreach (IBatchOperation operation in operations)
        {
            if (ImGui.Selectable(operation.DisplayName, operation.Id == _selectedId))
            {
                _selectedId = operation.Id;
            }

            if (!string.IsNullOrEmpty(operation.Description) && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(operation.Description);
            }
        }
    }

    private void DrawSelected(List<IBatchOperation> operations, bool running)
    {
        if (operations.FirstOrDefault(candidate => candidate.Id == _selectedId) is not { } operation)
        {
            return;
        }

        ImGui.TextUnformatted(operation.DisplayName);
        if (!string.IsNullOrEmpty(operation.Description))
        {
            ImGui.TextWrapped(operation.Description);
        }

        ImGui.Separator();

        // Settings stay editable while a run is in flight: the run snapshotted them when it started
        // and never reads these fields again.
        operation.DrawSettings();
        PersistSettingsIfChanged(operation);

        ImGui.Separator();

        // Checked up front rather than left to TryStart's refusal, so a disabled button and its reason
        // show before the click instead of after it.
        string? blocker = running ? null : _batch.Context.Operations.Blocker;

        ImGui.BeginDisabled(running || blocker != null);
        if (ImGui.Button("Run", new NVector2(130.0f, 0.0f)))
        {
            _running = _batch.TryStart(operation, overrides: null, out _);
            _popupOpenRequested = _running != null;
        }

        ImGui.EndDisabled();

        if (blocker != null)
        {
            ImGui.TextColored(MutedColor, blocker);
        }

        DrawRecentRuns();
    }

    /// <summary>Writes the operation's fields back to <see cref="BatchState"/> when they actually
    /// change, rather than once per frame.</summary>
    private void PersistSettingsIfChanged(IBatchOperation operation)
    {
        string json = operation.SaveSettings().ToJsonString();
        if (_lastSavedSettingsId == operation.Id && json == _lastSavedSettingsJson)
        {
            return;
        }

        // A freshly selected operation is only recorded, never written — its fields were loaded from
        // storage and writing them straight back would be a pointless query per selection.
        if (_lastSavedSettingsId == operation.Id)
        {
            _batch.SaveSettings(operation);
        }

        _lastSavedSettingsId = operation.Id;
        _lastSavedSettingsJson = json;
    }

    private void DrawRecentRuns()
    {
        if (_batch.Runs.Count == 0 || !ImGui.CollapsingHeader("Recent runs"))
        {
            return;
        }

        foreach (BatchRun run in _batch.Runs)
        {
            WorkSnapshot snapshot = run.Work?.Snapshot() ?? default;
            BatchStatus status = run.Status();
            string summary = string.IsNullOrEmpty(status.Message) ? snapshot.Step : status.Message;
            ImGui.TextColored(
                snapshot.State == WorkState.Faulted ? FaultedColor : MutedColor,
                $"{run.OperationId} — {snapshot.State} ({FormatElapsed(snapshot.ElapsedSeconds)}) {summary}");
        }
    }

    /// <summary>
    /// The whole recovery story for a session whose driver crashed or disconnected. Nothing times a
    /// session out, at any duration — a batch that runs for minutes is the normal case and a timer
    /// cannot tell that apart from a dead client — so a person reads these two clocks and decides.
    /// </summary>
    private void DrawSessionBanner()
    {
        if (_batch.ActiveSession is not { IsOpen: true } session)
        {
            return;
        }

        // A run draws the progress modal instead; this is for the sessions nothing else is showing.
        if (_running is { Work: { } handle } && handle.Snapshot().IsActive)
        {
            return;
        }

        ImGui.SetNextWindowPos(ImGui.GetMainViewport().WorkPos + new NVector2(16.0f, 16.0f), ImGuiCond.Always);
        if (ImGui.Begin("##batch-session", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoFocusOnAppearing))
        {
            DateTime now = DateTime.UtcNow;
            ImGui.Text($"Batch session '{session.Name}' holds the editor.");
            ImGui.TextColored(
                MutedColor,
                $"Open {FormatElapsed((now - session.OpenedUtc).TotalSeconds)}, "
                + $"last activity {FormatElapsed((now - session.LastActivityUtc).TotalSeconds)} ago");

            BatchStatus status = session.Status();
            if (!string.IsNullOrEmpty(status.Step))
            {
                ImGui.TextUnformatted(status.Step);
            }

            if (session.ReloadRequired)
            {
                ImGui.TextColored(MutedColor, "The world will reload when it ends.");
            }

            if (ImGui.Button("Force end"))
            {
                _batch.ForceEnd();
            }
        }

        ImGui.End();
    }

    /// <summary>A modal, not inline text: the editor really is unusable for the run's duration, and
    /// saying so is more honest than a UI that looks live but refuses every edit. Redraws every frame
    /// while the body runs on a worker, which is the only reason a minutes-long batch is watchable.</summary>
    private void DrawProgressPopup()
    {
        if (_running is not { Work: { } handle } run)
        {
            return;
        }

        WorkSnapshot snapshot = handle.Snapshot();
        BatchStatus status = run.Status();

        NVector2 center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new NVector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new NVector2(520.0f, 0.0f), ImGuiCond.Always);

        bool open = true;
        ImGuiEx.PopupModal(
            ProgressPopupId,
            snapshot.IsFinished,
            ref open,
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove,
            () =>
            {
                ImGui.Text(snapshot.Name);
                ImGui.Separator();

                DrawState(snapshot, status);
                DrawProgressBar(snapshot, status);

                if (!string.IsNullOrEmpty(status.Message))
                {
                    ImGui.TextWrapped(status.Message);
                }

                if (run.ReloadRequired)
                {
                    ImGui.TextColored(MutedColor, "The world will reload when this finishes.");
                }

                DrawLog(status);

                ImGui.Spacing();
                if (snapshot.IsActive)
                {
                    if (ImGui.Button("Cancel", new NVector2(120.0f, 0.0f)))
                    {
                        run.Cancel();
                    }
                }
                else if (ImGui.Button("OK", new NVector2(120.0f, 0.0f)))
                {
                    open = false;
                }
            });

        // An early dismissal while still active is ignored: the batch keeps running either way, so
        // dropping the handle here would only make it invisible. Only a finished run's own OK (or
        // Escape, once it can be closed) clears it.
        if (!open && snapshot.IsFinished)
        {
            _running = null;
        }
    }

    private static void DrawState(WorkSnapshot snapshot, BatchStatus status)
    {
        switch (snapshot.State)
        {
            case WorkState.Queued:
                ImGui.TextColored(MutedColor, "Queued…");
                break;
            case WorkState.Executing:
                ImGui.Text(string.IsNullOrEmpty(status.Step) ? "Running…" : status.Step);
                ImGui.TextColored(MutedColor, $"{FormatElapsed(snapshot.ElapsedSeconds)} elapsed");
                break;
            case WorkState.Completed:
                ImGui.TextColored(CompletedColor, $"Finished in {FormatElapsed(snapshot.ElapsedSeconds)}.");
                break;
            case WorkState.Faulted:
                ImGui.TextColored(FaultedColor, $"Failed after {FormatElapsed(snapshot.ElapsedSeconds)}:");
                ImGui.TextWrapped(snapshot.Error);
                break;
            case WorkState.Cancelled:
                ImGui.TextColored(MutedColor, $"Cancelled after {FormatElapsed(snapshot.ElapsedSeconds)}.");
                break;
        }
    }

    private static void DrawProgressBar(WorkSnapshot snapshot, BatchStatus status)
    {
        if (!snapshot.IsActive)
        {
            return;
        }

        // Negative fraction is ImGui's indeterminate bar — the right thing for an operation that
        // genuinely does not know its total, as opposed to one pinned at empty.
        float fraction = status.Progress ?? -1.0f * (float)ImGui.GetTime();
        string overlay = status.Progress is { } value ? $"{value * 100.0f:0}%" : "";
        ImGui.ProgressBar(fraction, new NVector2(-1.0f, 0.0f), overlay);
    }

    private static void DrawLog(BatchStatus status)
    {
        if (status.Log.Count == 0)
        {
            return;
        }

        if (ImGui.BeginChild("log", new NVector2(0.0f, 140.0f), true))
        {
            foreach (string line in status.Log)
            {
                ImGui.TextUnformatted(line);
            }

            // Follow the tail while it is being written, but leave the user free to scroll back.
            if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
            {
                ImGui.SetScrollHereY(1.0f);
            }
        }

        ImGui.EndChild();
    }

    private static string FormatElapsed(double seconds) => seconds switch
    {
        < 1.0 => $"{seconds * 1000.0:0} ms",
        < 60.0 => $"{seconds:0.0} s",
        _ => $"{seconds / 60.0:0.0} min",
    };
}
