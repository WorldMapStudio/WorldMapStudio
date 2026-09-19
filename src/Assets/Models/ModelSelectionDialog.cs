using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

public sealed class ModelSelectionDialog : IModalDialog<ModelSelectionContext>
{
    private static readonly Vector2 BodySize = new(900, 480);
    private static readonly Vector2 PreviewSize = new(320, 320);
    private const int LargeSetThreshold = 500;
    private const int MinFilterLengthForLargeSets = 2;
    private const float MinThumbnailSize = 64.0f;
    private const float MaxThumbnailSize = 192.0f;

    private string _filter = "";
    private string _previewPath = "";
    private List<AssetRef>? _models;
    private Task<IReadOnlyList<AssetRef>>? _modelsTask;
    private List<AssetRef> _filtered = [];
    private List<AssetRef>? _filteredSourceModels;
    private string _filteredForFilter = "";
    private ModelPreviewRenderer? _preview;
    private bool _gridView = true;
    private float _thumbnailSize = 128.0f;

    public ModalDialogState Draw(ModelSelectionContext context)
    {
        _preview ??= new ModelPreviewRenderer(context.Assets, context.Materials, context.PreviewOwner);
        if (_previewPath.Length == 0)
        {
            _previewPath = context.CurrentPath;
        }

        ImGui.Text("Select 3D Model");
        ImGui.Separator();

        if (ImGui.Button("Refresh"))
        {
            _models = null;
            _modelsTask = null;
            context.Assets.ClearModelCache();
            _previewPath = "";
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(BodySize.X - 140.0f);
        ImGui.InputTextWithHint("##filter", "Filter models...", ref _filter, 128);

        ImGui.Checkbox("Grid view", ref _gridView);
        if (_gridView)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(160.0f);
            ImGui.SliderFloat("Thumbnail size", ref _thumbnailSize, MinThumbnailSize, MaxThumbnailSize, "%.0f");
        }

        _modelsTask ??= context.Assets.ListModelAssetsAsync();
        if (_models == null && _modelsTask.IsCompletedSuccessfully)
        {
            _models = _modelsTask.Result
                .OrderBy(model => model.SourceName)
                .ThenBy(model => model.Path)
                .ToList();
        }

        ImGui.BeginChild("ModelAssetBody", BodySize, true, ImGuiWindowFlags.None);
        bool confirmed = _gridView ? DrawGrid(context) : DrawList(context);
        ImGui.SameLine();
        DrawPreviewPanel(context);
        ImGui.EndChild();

        if (confirmed)
        {
            return ModalDialogState.Confirmed;
        }

        ImGui.Separator();
        if (ImGui.Button("Clear", new Vector2(120, 0)))
        {
            context.Select("");
            return ModalDialogState.Confirmed;
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
            return ModalDialogState.Confirmed;
        }

        if (!canSelect)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120, 0)))
        {
            return ModalDialogState.Cancelled;
        }

        return ModalDialogState.Running;
    }

    public void OnClose()
    {
        _preview?.Dispose();
        _preview = null;
        _previewPath = "";
    }

    private bool DrawList(ModelSelectionContext context)
    {
        Vector2 listSize = new(BodySize.X - PreviewSize.X - 24.0f, BodySize.Y - 8.0f);
        ImGui.BeginChild("ModelAssetList", listSize, true, ImGuiWindowFlags.None);

        if (_models == null)
        {
            ImGui.TextDisabled("Indexing models...");
            ImGui.EndChild();
            return false;
        }

        RefreshFilteredModels();

        int totalCount = _models?.Count ?? 0;
        if (totalCount > LargeSetThreshold && _filter.Trim().Length < MinFilterLengthForLargeSets)
        {
            ImGui.TextDisabled($"{totalCount} models. Type at least {MinFilterLengthForLargeSets} characters to filter.");
            ImGui.EndChild();
            return false;
        }

        if (_filtered.Count == 0)
        {
            ImGui.TextDisabled(totalCount == 0 ? "No model assets found." : "No models match the filter.");
            ImGui.EndChild();
            return false;
        }

        ImGui.TextDisabled($"{_filtered.Count} models");

        unsafe
        {
            var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
            clipper.Begin(_filtered.Count);
            while (clipper.Step())
            {
                for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    AssetRef model = _filtered[i];
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
            }

            clipper.End();
            clipper.Destroy();
        }

        ImGui.EndChild();
        return false;
    }

    private bool DrawGrid(ModelSelectionContext context)
    {
        Vector2 gridSize = new(BodySize.X - PreviewSize.X - 24.0f, BodySize.Y - 8.0f);

        if (_models == null)
        {
            ImGui.BeginChild("ModelAssetGrid", gridSize, true, ImGuiWindowFlags.None);
            ImGui.TextDisabled("Indexing models...");
            ImGui.EndChild();
            return false;
        }

        RefreshFilteredModels();
        context.Assets.ModelThumbnails.BeginFrame();

        int totalCount = _models.Count;
        if (totalCount > LargeSetThreshold && _filter.Trim().Length < MinFilterLengthForLargeSets)
        {
            ImGui.BeginChild("ModelAssetGrid", gridSize, true, ImGuiWindowFlags.None);
            ImGui.TextDisabled($"{totalCount} models. Type at least {MinFilterLengthForLargeSets} characters to filter.");
            ImGui.EndChild();
            context.Assets.ModelThumbnails.Update();
            return false;
        }

        float cardWidth = _thumbnailSize + AssetCardGrid.Padding * 2.0f;
        AssetCardGrid.CardClick? click = AssetCardGrid.Draw(
            "ModelAssetGrid",
            gridSize,
            _filtered,
            cardWidth,
            _thumbnailSize,
            asset => asset.Path == _previewPath,
            (asset, min, max) => DrawModelThumbnail(context, asset, min, max));

        context.Assets.ModelThumbnails.Update();

        if (click == null)
        {
            return false;
        }

        _previewPath = click.Value.Asset.Path;
        if (!click.Value.DoubleClick)
        {
            return false;
        }

        context.Select(_previewPath);
        return true;
    }

    private static void DrawModelThumbnail(ModelSelectionContext context, AssetRef asset, Vector2 min, Vector2 max)
    {
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        IntPtr textureId = context.Assets.ModelThumbnails.TextureId(asset.Path, out bool failed);
        if (textureId != IntPtr.Zero)
        {
            draw.AddImage(textureId, min, max);
            return;
        }

        draw.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.WindowBg), 2.0f);
        string label = failed ? "No preview" : "Loading";
        Vector2 textSize = ImGui.CalcTextSize(label);
        draw.AddText((min + max) * 0.5f - textSize * 0.5f, ImGui.GetColorU32(ImGuiCol.TextDisabled), label);
    }

    private void RefreshFilteredModels()
    {
        if (_models == null)
        {
            _filtered = [];
            _filteredSourceModels = null;
            return;
        }

        string filter = _filter.Trim();
        if (ReferenceEquals(_models, _filteredSourceModels) && filter == _filteredForFilter)
        {
            return;
        }

        _filteredSourceModels = _models;
        _filteredForFilter = filter;
        _filtered = filter.Length == 0 ? _models : _models.Where(model =>
            model.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            model.SourceName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            model.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
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

}
