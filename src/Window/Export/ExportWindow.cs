using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

public enum ExportTargetMode
{
    Dirty,
    Range,
}

[Subsystem(nameof(WindowManager))]
public sealed class ExportWindow : Window
{
    public override string? Category => "World";

    private const string ProgressPopupId = "Export Progress";
    private const string CreatePopupId = "New Export Profile";
    private const string RenamePopupId = "Rename Export Profile";

    private static readonly NVector4 FaultedColor = new(1.0f, 0.45f, 0.40f, 1.0f);
    private static readonly NVector4 CompletedColor = new(0.42f, 0.85f, 0.46f, 1.0f);
    private static readonly NVector4 MutedColor = new(0.60f, 0.60f, 0.60f, 1.0f);

    private readonly ExportSystem _exports;

    private string? _selectedProfileId;
    private string? _loadedSettingsProfileId;
    private string _lastSavedSettingsJson = "";

    private ExportTargetMode _mode;
    private ChunkExportScope _scope;
    private int _rangeMapIndex;
    private int _rangeMinX, _rangeMinY, _rangeMaxX, _rangeMaxY;

    private bool _createOpenRequested;
    private bool _createActive;
    private string _createName = "";
    private int _createExporterIndex;

    private bool _renameOpenRequested;
    private bool _renameActive;
    private string _renameBuffer = "";

    private WorkHandle? _running;
    private bool _popupOpenRequested;

    public ExportWindow(WindowManager manager)
        : base("Export", startOpen: false, defaultSize: new NVector2(480.0f, 400.0f))
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
        IReadOnlyList<ExportProfile> profiles = _exports.Profiles.Profiles;
        bool running = _running is { } handle && handle.Snapshot().IsActive;

        ImGui.BeginDisabled(running);
        DrawProfileSelector(profiles, exporters);
        ImGui.EndDisabled();

        DrawCreatePopup(exporters);

        ExportProfile? profile = profiles.FirstOrDefault(candidate => candidate.Id == _selectedProfileId);
        DrawRenamePopup(profile);

        if (profile == null)
        {
            ImGui.TextDisabled(exporters.Count == 0
                ? "No exporters registered."
                : "Create a profile to configure and run an export.");
            return;
        }

        if (exporters.FirstOrDefault(candidate => candidate.Id == profile.ExporterId) is not { } exporter)
        {
            ImGui.TextColored(FaultedColor, $"Exporter '{profile.ExporterId}' is not registered (plugin missing?).");
            return;
        }

        CheckoutSettings(exporter, profile);

        ImGui.BeginDisabled(running);

        int mode = (int)_mode;
        if (ImGui.Combo("Target", ref mode, "Dirty\0Range\0"))
        {
            _mode = (ExportTargetMode)mode;
        }

        int dirty = 0;
        ChunkRange? range = null;

        if (_mode == ExportTargetMode.Dirty)
        {
            int scope = (int)_scope;
            if (ImGui.Combo("Scope", ref scope, "Current map\0All maps\0"))
            {
                _scope = (ChunkExportScope)scope;
            }

            dirty = _exports.Changes.DirtyFor(profile.Id, _scope).Count;
            ImGui.TextDisabled($"{dirty} dirty chunks");
        }
        else
        {
            range = DrawRangeControls(_exports.Context.Maps.Maps);
        }

        ImGui.Separator();
        exporter.DrawSettings();
        ImGui.Separator();

        ImGui.EndDisabled();

        CheckinSettings(exporter, profile);

        // Checked up front, not just left to Run()'s own refusal, so a disabled button and its reason
        // show before the click rather than only a console warning after it.
        string? blocker = running ? null : _exports.Context.Operations.Blocker;

        if (_mode == ExportTargetMode.Dirty)
        {
            ImGui.BeginDisabled(running || dirty == 0 || blocker != null);
            if (ImGui.Button("Export Dirty", new NVector2(130.0f, 0.0f)))
            {
                _running = _exports.Run(profile, _scope);
                _popupOpenRequested = _running != null;
            }
            ImGui.EndDisabled();
        }
        else
        {
            ImGui.BeginDisabled(running || range == null || blocker != null);
            if (ImGui.Button("Export Range", new NVector2(130.0f, 0.0f)))
            {
                _running = _exports.RunRange(profile, range!.Value);
                _popupOpenRequested = _running != null;
            }
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(running);
        if (ImGui.Button("Clear Dirty", new NVector2(130.0f, 0.0f)))
        {
            if (_mode == ExportTargetMode.Dirty)
            {
                _exports.ClearDirty(profile, _scope);
            }
            else if (range is { } clearRange)
            {
                _exports.ClearDirty(profile, clearRange);
            }
        }
        ImGui.EndDisabled();

        if (blocker != null)
        {
            ImGui.TextColored(MutedColor, blocker);
        }
    }

    private void DrawProfileSelector(IReadOnlyList<ExportProfile> profiles, List<IChunkExportScript> exporters)
    {
        if (_selectedProfileId == null || profiles.All(candidate => candidate.Id != _selectedProfileId))
        {
            _selectedProfileId = profiles.FirstOrDefault()?.Id;
        }

        ExportProfile? selected = profiles.FirstOrDefault(candidate => candidate.Id == _selectedProfileId);
        string preview = selected?.Name ?? "(none)";
        if (ImGui.BeginCombo("Profile", preview))
        {
            foreach (ExportProfile candidate in profiles)
            {
                bool isSelected = candidate.Id == _selectedProfileId;
                if (ImGui.Selectable(candidate.Name, isSelected))
                {
                    _selectedProfileId = candidate.Id;
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(exporters.Count == 0);
        if (ImGui.Button("+"))
        {
            _createName = "";
            _createExporterIndex = 0;
            _createOpenRequested = true;
            _createActive = true;
        }
        ImGui.EndDisabled();

        if (selected == null)
        {
            return;
        }

        ImGui.SameLine();
        if (ImGui.Button("Rename"))
        {
            _renameBuffer = selected.Name;
            _renameOpenRequested = true;
            _renameActive = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Delete"))
        {
            _exports.Profiles.Delete(selected);
            _selectedProfileId = null;
        }
    }

    private void DrawCreatePopup(List<IChunkExportScript> exporters)
    {
        if (_createOpenRequested)
        {
            ImGui.OpenPopup(CreatePopupId);
            _createOpenRequested = false;
        }

        if (!_createActive)
        {
            return;
        }

        bool open = true;
        ImGuiEx.PopupModal(CreatePopupId, true, ref open, ImGuiWindowFlags.AlwaysAutoResize, () =>
        {
            ImGui.InputText("Name", ref _createName, 128);

            _createExporterIndex = System.Math.Clamp(_createExporterIndex, 0, exporters.Count - 1);
            if (ImGui.BeginCombo("Exporter", exporters[_createExporterIndex].DisplayName))
            {
                for (int i = 0; i < exporters.Count; i++)
                {
                    bool isSelected = i == _createExporterIndex;
                    if (ImGui.Selectable(exporters[i].DisplayName, isSelected))
                    {
                        _createExporterIndex = i;
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(_createName));
            if (ImGui.Button("Create", new NVector2(120.0f, 0.0f)))
            {
                IChunkExportScript exporter = exporters[_createExporterIndex];
                ExportProfile created = _exports.Profiles.Create(_createName.Trim(), exporter.Id, exporter.SaveSettings());
                _selectedProfileId = created.Id;
                open = false;
            }
            ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new NVector2(120.0f, 0.0f)))
            {
                open = false;
            }
        });

        if (!open)
        {
            _createActive = false;
        }
    }

    private void DrawRenamePopup(ExportProfile? profile)
    {
        if (_renameOpenRequested)
        {
            ImGui.OpenPopup(RenamePopupId);
            _renameOpenRequested = false;
        }

        if (!_renameActive)
        {
            return;
        }

        bool open = true;
        ImGuiEx.PopupModal(RenamePopupId, true, ref open, ImGuiWindowFlags.AlwaysAutoResize, () =>
        {
            ImGui.InputText("Name", ref _renameBuffer, 128);

            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(_renameBuffer));
            if (ImGui.Button("Rename", new NVector2(120.0f, 0.0f)))
            {
                if (profile != null)
                {
                    _exports.Profiles.Rename(profile, _renameBuffer.Trim());
                }

                open = false;
            }
            ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new NVector2(120.0f, 0.0f)))
            {
                open = false;
            }
        });

        if (!open)
        {
            _renameActive = false;
        }
    }

    private ChunkRange? DrawRangeControls(IReadOnlyList<Map> maps)
    {
        if (maps.Count == 0)
        {
            ImGui.TextDisabled("No maps available.");
            return null;
        }

        _rangeMapIndex = System.Math.Clamp(_rangeMapIndex, 0, maps.Count - 1);
        if (ImGui.BeginCombo("Map", maps[_rangeMapIndex].DisplayName))
        {
            for (int i = 0; i < maps.Count; i++)
            {
                bool isSelected = i == _rangeMapIndex;
                if (ImGui.Selectable(maps[i].DisplayName, isSelected))
                {
                    _rangeMapIndex = i;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.InputInt("Min X", ref _rangeMinX);
        ImGui.InputInt("Min Y", ref _rangeMinY);
        ImGui.InputInt("Max X", ref _rangeMaxX);
        ImGui.InputInt("Max Y", ref _rangeMaxY);
        _rangeMaxX = System.Math.Max(_rangeMaxX, _rangeMinX);
        _rangeMaxY = System.Math.Max(_rangeMaxY, _rangeMinY);

        int chunkCount = (_rangeMaxX - _rangeMinX + 1) * (_rangeMaxY - _rangeMinY + 1);
        ImGui.TextDisabled($"{chunkCount} chunk(s) in range");

        return new ChunkRange(
            maps[_rangeMapIndex].Id,
            new ChunkCoord(_rangeMinX, _rangeMinY),
            new ChunkCoord(_rangeMaxX, _rangeMaxY));
    }

    /// <summary>Loads a newly-selected profile's settings into the (singleton) exporter instance's own
    /// fields, since <see cref="IChunkExportScript.DrawSettings"/> still renders those fields directly —
    /// a no-op once the exporter already reflects this profile.</summary>
    private void CheckoutSettings(IChunkExportScript exporter, ExportProfile profile)
    {
        if (_loadedSettingsProfileId == profile.Id)
        {
            return;
        }

        exporter.LoadSettings(profile.Settings);
        _loadedSettingsProfileId = profile.Id;
        _lastSavedSettingsJson = profile.Settings.ToJsonString();
    }

    /// <summary>Persists the exporter's current field values back into the profile whenever they've
    /// actually changed since the last checkout/checkin, rather than writing to disk every frame.</summary>
    private void CheckinSettings(IChunkExportScript exporter, ExportProfile profile)
    {
        JsonObject settings = exporter.SaveSettings();
        string json = settings.ToJsonString();
        if (json == _lastSavedSettingsJson)
        {
            return;
        }

        _exports.Profiles.SaveSettings(profile, settings);
        _lastSavedSettingsJson = json;
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
