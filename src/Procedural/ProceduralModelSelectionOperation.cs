using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// The searchable "pick a procedural model" popup, modelled on <see cref="ModelSelectionOperation"/>.
/// Unlike that one there is no background indexing task — the catalog is already loaded in memory —
/// and no large-set filter gate, since this catalog is hand-authored rather than an MPQ's worth of paths.
/// </summary>
public sealed class ProceduralModelSelectionOperation : IModalOperation<ProceduralModelSelectionContext>
{
    private static readonly Vector2 BodySize = new(900, 480);
    private static readonly Vector2 PreviewSize = new(320, 320);

    private string _filter = "";
    private int? _previewId;
    private bool _previewIdSet;
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
        ImGui.InputTextWithHint("##filter", "Filter models...", ref _filter, 128);

        ImGui.BeginChild("ProceduralModelBody", BodySize, true, ImGuiWindowFlags.None);
        DrawList(context);
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
    }

    private void DrawList(ProceduralModelSelectionContext context)
    {
        Vector2 listSize = new(BodySize.X - PreviewSize.X - 24.0f, BodySize.Y - 8.0f);
        ImGui.BeginChild("ProceduralModelList", listSize, true, ImGuiWindowFlags.None);

        List<ProceduralModel> models = FilteredModels(context);
        if (models.Count == 0)
        {
            ImGui.TextDisabled(context.System.Models.Any() ? "No models match the filter." : "No procedural models yet.");
            ImGui.EndChild();
            return;
        }

        ImGui.TextDisabled($"{models.Count} models");

        foreach (ProceduralModel model in models)
        {
            IProceduralFunction? bound = context.System.Find(model.FunctionId);
            string function = bound?.DisplayName ?? "(missing function)";
            string outputs = bound == null ? "" : $" · {bound.Outputs.Count} outputs";
            int uses = context.System.UsageCount(model.RecordId ?? -1);
            bool selected = model.RecordId == _previewId;
            if (ImGui.Selectable($"#{model.RecordId} · {model.Name} · {function}{outputs} · {uses} uses##{model.RecordId}", selected))
            {
                _previewId = model.RecordId;
            }
        }

        ImGui.EndChild();
    }

    private List<ProceduralModel> FilteredModels(ProceduralModelSelectionContext context)
    {
        string filter = _filter.Trim();
        IEnumerable<ProceduralModel> models = context.System.Models.OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase);
        if (filter.Length == 0)
        {
            return models.ToList();
        }

        return models.Where(model =>
            model.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            (model.RecordId?.ToString() == filter) ||
            (context.System.Find(model.FunctionId)?.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }

    private void DrawPreviewPanel(ProceduralModelSelectionContext context)
    {
        ImGui.BeginGroup();
        ProceduralModel? model = _previewId is int id ? context.System.FindModel(id) : null;
        string current = model == null ? "(none)" : $"#{model.RecordId} {model.Name}";
        ImGui.TextDisabled($"Preview: {current}");

        string cacheKey = model == null ? "" : $"{model.RecordId}|{model.Revision}|{context.System.Context.MeshMaterials.PresetContentVersion}";
        ModelAsset? built = model != null ? context.System.Build(model).ToPreviewAsset() : null;
        _preview!.DrawAsset(cacheKey, built, PreviewSize);

        ProceduralModel? currentModel = context.CurrentId is int currentId ? context.System.FindModel(currentId) : null;
        ImGui.TextDisabled(currentModel == null ? "Current: (none)" : $"Current: #{currentModel.RecordId} {currentModel.Name}");
        ImGui.EndGroup();
    }
}
