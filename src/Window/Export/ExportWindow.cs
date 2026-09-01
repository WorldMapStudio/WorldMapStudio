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

    private static readonly NVector4 FaultedColor = new(1.0f, 0.45f, 0.40f, 1.0f);
    private static readonly NVector4 CompletedColor = new(0.42f, 0.85f, 0.46f, 1.0f);
    private static readonly NVector4 MutedColor = new(0.60f, 0.60f, 0.60f, 1.0f);

    private readonly ExportSystem _exports;
    private int _selected;
    private ChunkExportScope _scope;
    private WorkHandle? _running;

    public ExportWindow(WindowManager manager)
        : base("Export", startOpen: false, defaultSize: new NVector2(480.0f, 360.0f))
    {
        _exports = manager.Context.Exports;
    }

    protected override void DrawContent()
    {
        List<IChunkExportScript> exporters = _exports.Exporters.ToList();
        if (exporters.Count == 0)
        {
            ImGui.TextDisabled("No exporters registered.");
            return;
        }

        WorkSnapshot? snapshot = _running?.Snapshot();
        bool running = snapshot is { IsActive: true };

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
        }
        ImGui.EndDisabled();

        if (running)
        {
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                _running!.Cancel();
            }
        }
        else if (blocker != null)
        {
            ImGui.TextColored(MutedColor, blocker);
        }

        DrawStatus(snapshot);
    }

    /// <summary>Shows what the held <see cref="_running"/> handle is doing (or last did) — the export
    /// itself blocks all editing while it runs (<see cref="WorldOperations"/>), so this is the only
    /// feedback the user has that anything is happening.</summary>
    private static void DrawStatus(WorkSnapshot? snapshot)
    {
        if (snapshot is not { } s)
        {
            return;
        }

        ImGui.Separator();
        switch (s.State)
        {
            case WorkState.Queued:
                ImGui.TextColored(MutedColor, "Queued…");
                break;
            case WorkState.Executing:
                ImGui.Text(string.IsNullOrEmpty(s.Step) ? "Exporting…" : s.Step);
                ImGui.TextColored(MutedColor, $"{s.Name}   ({FormatElapsed(s.ElapsedSeconds)} elapsed)");
                break;
            case WorkState.Completed:
                ImGui.TextColored(CompletedColor, $"Finished '{s.Name}' in {FormatElapsed(s.ElapsedSeconds)}.");
                break;
            case WorkState.Faulted:
                ImGui.TextColored(FaultedColor, $"Failed after {FormatElapsed(s.ElapsedSeconds)}: {s.Error}");
                break;
            case WorkState.Cancelled:
                ImGui.TextColored(MutedColor, $"Cancelled after {FormatElapsed(s.ElapsedSeconds)}.");
                break;
        }
    }

    private static string FormatElapsed(double seconds) =>
        seconds < 1.0 ? $"{seconds * 1000.0:0} ms" : $"{seconds:0.0} s";
}
