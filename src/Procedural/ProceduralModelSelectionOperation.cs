using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// The searchable "pick a procedural model" popup, modelled on <see cref="ModelSelectionOperation"/>.
/// Unlike that one, the catalog is not indexed in memory — it is lazily loaded (see
/// <see cref="ProceduralModelFactory"/>) — so the result list comes from <see cref="ProceduralModelFactory.SearchAsync"/>,
/// debounced against typing, and only the row the user is actually previewing is ever opened
/// (<see cref="ProceduralModelFactory.OpenAsync"/>): a search result carries just an id and a label,
/// never a network.
/// </summary>
public sealed class ProceduralModelSelectionOperation : IModalOperation<ProceduralModelSelectionContext>
{
    private static readonly Vector2 BodySize = new(900, 480);
    private static readonly Vector2 PreviewSize = new(320, 320);
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(250.0);

    private string _filter = "";
    private string _queriedFilter = "￿"; // never a real filter, so the first search always fires
    private long _filterChangedAt;
    private Task<IReadOnlyList<CatalogSearchResult>>? _searchTask;
    private List<CatalogSearchResult> _results = [];

    private int? _previewId;
    private bool _previewIdSet;
    private int? _loadedPreviewId;
    private ProceduralModel? _previewModel;

    private ModelPreviewRenderer? _preview;

    public ModalOperationState Draw(ProceduralModelSelectionContext context)
    {
        _preview ??= new ModelPreviewRenderer(context.System.Context.Assets, context.System.Context.MeshMaterials, context.PreviewOwner);
        if (!_previewIdSet)
        {
            _previewId = context.CurrentId;
            _previewIdSet = true;
        }

        ImGui.Text("Select Procedural Model");
        ImGui.Separator();

        ImGui.SetNextItemWidth(BodySize.X);
        if (ImGui.InputTextWithHint("##filter", "Filter models...", ref _filter, 128))
        {
            _filterChangedAt = Stopwatch.GetTimestamp();
        }

        PumpSearch(context);
        PumpPreview(context);

        ImGui.BeginChild("ProceduralModelBody", BodySize, true, ImGuiWindowFlags.None);
        DrawList();
        ImGui.SameLine();
        DrawPreviewPanel(context);
        ImGui.EndChild();

        ImGui.Separator();
        if (ImGui.Button("Clear", new Vector2(120, 0)))
        {
            context.Select(null);
            return ModalOperationState.Confirmed;
        }

        ImGui.SameLine();
        bool canSelect = _previewId != null;
        if (!canSelect)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Select", new Vector2(120, 0)))
        {
            context.Select(_previewId);
            return ModalOperationState.Confirmed;
        }

        if (!canSelect)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120, 0)))
        {
            return ModalOperationState.Cancelled;
        }

        return ModalOperationState.Running;
    }

    public void OnClose()
    {
        _preview?.Dispose();
        _preview = null;
        _previewId = null;
        _previewIdSet = false;
        _previewModel = null;
        _loadedPreviewId = null;
        _filter = "";
        _queriedFilter = "￿";
        _searchTask = null;
        _results = [];
    }

    // Debounced: a search fires only once the filter text has sat unchanged for DebounceDelay, so
    // fast typing queues one query instead of one per keystroke.
    private void PumpSearch(ProceduralModelSelectionContext context)
    {
        if (_searchTask is { IsCompleted: true } completed)
        {
            _searchTask = null;
            if (completed.IsCompletedSuccessfully)
            {
                _results = completed.Result.ToList();
            }
        }

        if (_searchTask != null)
        {
            return;
        }

        string trimmed = _filter.Trim();
        if (trimmed == _queriedFilter || Stopwatch.GetElapsedTime(_filterChangedAt) < DebounceDelay)
        {
            return;
        }

        _queriedFilter = trimmed;
        _searchTask = context.System.ModelFactory.SearchAsync(trimmed);
    }

    // A discrete, user-driven act (selecting a row to preview) rather than a per-frame cost — the same
    // class of stall BlockingWork exists for.
    private void PumpPreview(ProceduralModelSelectionContext context)
    {
        if (_previewId == _loadedPreviewId)
        {
            return;
        }

        _loadedPreviewId = _previewId;
        _previewModel = _previewId is int id
            ? BlockingWork.Run(() => context.System.ModelFactory.OpenAsync(context.System.Context, id.ToString())) as ProceduralModel
            : null;
    }

    private void DrawList()
    {
        Vector2 listSize = new(BodySize.X - PreviewSize.X - 24.0f, BodySize.Y - 8.0f);
        ImGui.BeginChild("ProceduralModelList", listSize, true, ImGuiWindowFlags.None);

        if (_results.Count == 0)
        {
            ImGui.TextDisabled(_searchTask != null
                ? "Searching..."
                : _filter.Trim().Length == 0 ? "No procedural models yet." : "No models match the filter.");
            ImGui.EndChild();
            return;
        }

        ImGui.TextDisabled($"{_results.Count} models");

        foreach (CatalogSearchResult result in _results)
        {
            bool selected = _previewId is int id && id.ToString() == result.Key;
            if (ImGui.Selectable($"{result.Label}##{result.Key}", selected) && int.TryParse(result.Key, out int picked))
            {
                _previewId = picked;
            }
        }

        ImGui.EndChild();
    }

    private void DrawPreviewPanel(ProceduralModelSelectionContext context)
    {
        ImGui.BeginGroup();
        string current = _previewModel == null ? "(none)" : $"#{_previewModel.RecordId} {_previewModel.Name}";
        ImGui.TextDisabled($"Preview: {current}");

        string cacheKey = _previewModel == null ? "" : $"{_previewModel.RecordId}|{_previewModel.Revision}|{context.System.Context.MeshMaterials.PresetContentVersion}";
        ModelAsset? built = _previewModel != null ? context.System.Build(_previewModel).ToPreviewAsset() : null;
        _preview!.DrawAsset(cacheKey, built, PreviewSize);

        ProceduralModel? currentModel = context.CurrentId is int currentId ? context.System.FindModel(currentId) : null;
        ImGui.TextDisabled(currentModel == null
            ? context.CurrentId is int stillId ? $"Current: #{stillId}" : "Current: (none)"
            : $"Current: #{currentModel.RecordId} {currentModel.Name}");
        ImGui.EndGroup();
    }
}
