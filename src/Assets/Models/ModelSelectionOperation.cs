using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

public sealed class ModelSelectionOperation : IModalOperation<ModelSelectionContext>
{
    private static readonly Vector2 BodySize = new(900, 480);
    private static readonly Vector2 PreviewSize = new(320, 320);

    private string _filter = "";
    private string _previewPath = "";
    private List<AssetRef>? _models;
    private ModelPreviewRenderer? _preview;

    public ModalOperationState Draw(ModelSelectionContext context)
    {
        _preview ??= new ModelPreviewRenderer(context.Assets, context.PreviewOwner);
        if (_previewPath.Length == 0)
        {
            _previewPath = context.CurrentPath;
        }

        ImGui.Text("Select 3D Model");
        ImGui.Separator();

        if (ImGui.Button("Refresh"))
        {
            _models = null;
            context.Assets.ClearModelCache();
            _previewPath = "";
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(BodySize.X - 140.0f);
        ImGui.InputTextWithHint("##filter", "Filter models...", ref _filter, 128);

        _models ??= context.Assets.ListModelAssets()
            .OrderBy(model => model.SourceName)
            .ThenBy(model => model.Path)
            .ToList();

        ImGui.BeginChild("ModelAssetBody", BodySize, true, ImGuiWindowFlags.None);
        DrawList(context);
        ImGui.SameLine();
        DrawPreviewPanel(context);
        ImGui.EndChild();

        ImGui.Separator();
        if (ImGui.Button("Clear", new Vector2(120, 0)))
        {
            context.Select("");
            return ModalOperationState.Confirmed;
        }

        ImGui.SameLine();
        bool canSelect = _previewPath.Length > 0;
        if (!canSelect)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Select", new Vector2(120, 0)))
        {
            context.Select(_previewPath);
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
        _previewPath = "";
    }

    private void DrawList(ModelSelectionContext context)
    {
        Vector2 listSize = new(BodySize.X - PreviewSize.X - 24.0f, BodySize.Y - 8.0f);
        ImGui.BeginChild("ModelAssetList", listSize, true, ImGuiWindowFlags.None);

        List<AssetRef> visible = FilteredModels().ToList();
        if (visible.Count == 0)
        {
            ImGui.TextDisabled(_models?.Count == 0 ? "No model assets found." : "No models match the filter.");
            ImGui.EndChild();
            return;
        }

        foreach (AssetRef model in visible)
        {
            bool selected = model.Path == _previewPath;
            if (ImGui.Selectable($"{model.DisplayName}##{model.Path}", selected))
            {
                _previewPath = model.Path;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(model.FullPath);
            }
        }

        ImGui.EndChild();
    }

    private void DrawPreviewPanel(ModelSelectionContext context)
    {
        ImGui.BeginGroup();
        string current = _previewPath.Length == 0 ? "(none)" : _previewPath;
        ImGui.TextDisabled($"Preview: {current}");
        _preview!.Draw(_previewPath, PreviewSize);
        ImGui.TextDisabled(context.CurrentPath.Length == 0 ? "Current: (none)" : $"Current: {context.CurrentPath}");
        ImGui.EndGroup();
    }

    private IEnumerable<AssetRef> FilteredModels()
    {
        if (_models == null)
        {
            return [];
        }

        string filter = _filter.Trim();
        if (filter.Length == 0)
        {
            return _models;
        }

        return _models.Where(model =>
            model.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            model.SourceName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            model.Path.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }
}
