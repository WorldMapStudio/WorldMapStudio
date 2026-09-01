using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

[Subsystem(nameof(WindowManager))]
public sealed class ExportWindow : Window
{
    public override string? Category => "World";

    private const string ProgressPopupId = "Export Progress";

    private static readonly NVector4 FaultedColor = new(1.0f, 0.45f, 0.40f, 1.0f);
    private static readonly NVector4 CompletedColor = new(0.42f, 0.85f, 0.46f, 1.0f);
    private static readonly NVector4 MutedColor = new(0.60f, 0.60f, 0.60f, 1.0f);

    private readonly ExportSystem _exports;
    private int _selected;
    private ChunkExportScope _scope;
    private WorkHandle? _running;
    private bool _popupOpenRequested;

    public ExportWindow(WindowManager manager)
        : base("Export", startOpen: false, defaultSize: new NVector2(480.0f, 360.0f))
    {
        _exports = manager.Context.Exports;
    }

    /// <summary>Runs even while this window is closed, so the popup stays visible (and editing stays
    /// blocked) regardless of whether the user has the Export window open — the same reason the
    /// underlying <see cref="WorldOperations"/> gate isn't scoped to this window either.</summary>
    protected override void OnBeforeDraw()
    {
        if (_popupOpenRequested)
        {
            ImGui.OpenPopup(ProgressPopupId);
            _popupOpenRequested = false;
        }

        DrawProgressPopup();
    }

    protected override void DrawContent()
    {
        List<IChunkExportScript> exporters = _exports.Exporters.ToList();
        if (exporters.Count == 0)
        {
            ImGui.TextDisabled("No exporters registered.");
            return;
        }

        bool running = _running is { } handle && handle.Snapshot().IsActive;

        ImGui.BeginDisabled(running);
        _selected = System.Math.Clamp(_selected, 0, exporters.Count - 1);
        string preview = exporters[_selected].DisplayName;
        if (ImGui.BeginCombo("Exporter", preview))
        {
            for (int i = 0; i < exporters.Count; i++)
            {
                bool selected = i == _selected;
                if (ImGui.Selectable(exporters[i].DisplayName, selected))
                {
                    _selected = i;
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        int scope = (int)_scope;
        if (ImGui.Combo("Scope", ref scope, "Current map\0All maps\0"))
        {
            _scope = (ChunkExportScope)scope;
        }

        IChunkExportScript exporter = exporters[_selected];
        int dirty = _exports.Changes.DirtyFor(exporter.Id, _scope).Count;
        ImGui.TextDisabled($"{dirty} dirty chunks");

        ImGui.Separator();
        exporter.DrawSettings();
        ImGui.Separator();
        ImGui.EndDisabled();

        // Checked up front, not just left to Run()'s own refusal, so a disabled button and its reason
        // show before the click rather than only a console warning after it.
        string? blocker = running ? null : _exports.Context.Operations.Blocker;

        ImGui.BeginDisabled(running || dirty == 0 || blocker != null);
        if (ImGui.Button("Export Dirty", new NVector2(130.0f, 0.0f)))
        {
            _running = _exports.Run(exporter, _scope);
            _popupOpenRequested = _running != null;
        }
        ImGui.EndDisabled();

        if (blocker != null)
        {
            ImGui.TextColored(MutedColor, blocker);
        }
    }

    /// <summary>The actual progress feedback — a blocking modal, not inline window text, so it's
    /// visible (and the app reads as busy) no matter which window has focus, matching the fact that
    /// <see cref="ExportSystem.Run"/> now blocks all editing for the same duration
    /// (<see cref="WorldOperations"/>).</summary>
    private void DrawProgressPopup()
    {
        if (_running is not { } handle)
        {
            return;
        }

        WorkSnapshot snapshot = handle.Snapshot();

        NVector2 center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new NVector2(0.5f, 0.5f));

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

                switch (snapshot.State)
                {
                    case WorkState.Queued:
                        ImGui.TextColored(MutedColor, "Queued…");
                        break;
                    case WorkState.Executing:
                        ImGui.Text(string.IsNullOrEmpty(snapshot.Step) ? "Exporting…" : snapshot.Step);
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

                ImGui.Spacing();
                if (snapshot.IsActive)
                {
                    if (ImGui.Button("Cancel", new NVector2(120.0f, 0.0f)))
                    {
                        handle.Cancel();
                    }
                }
                else if (ImGui.Button("OK", new NVector2(120.0f, 0.0f)))
                {
                    open = false;
                }
            });

        // Ignore an early dismissal attempt (e.g. the popup's own close button) while still active —
        // the export keeps running either way, so losing the handle here would just make it invisible
        // again, not stop it. Only a finished run's own "OK" (or Escape, once canBeClosed) actually
        // clears it.
        if (!open && snapshot.IsFinished)
        {
            _running = null;
        }
    }

    private static string FormatElapsed(double seconds) =>
        seconds < 1.0 ? $"{seconds * 1000.0:0} ms" : $"{seconds:0.0} s";
}
