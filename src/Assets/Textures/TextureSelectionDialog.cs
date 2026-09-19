using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

public sealed class TextureSelectionDialog : IModalDialog<TextureSelectionContext>
{
    private static readonly Vector2 BodySize = new(768, 432);

    private const float CardWidth = 160.0f;
    private const float ThumbnailSize = 128.0f;

    private string _filter = "";
    private TextureSelectionContext? _shown;
    private List<AssetRef>? _textures;
    private Task<IReadOnlyList<AssetRef>>? _texturesTask;
    private readonly Dictionary<string, Task<Texture2D?>> _previews = new();

    public ModalDialogState Draw(TextureSelectionContext context)
    {
        if (!ReferenceEquals(_shown, context))
        {
            _shown = context;
            _filter = context.InitialFilter;
        }

        ImGui.Text("Select Texture");
        ImGui.Separator();

        if (ImGui.Button("Refresh"))
        {
            _textures = null;
            _texturesTask = null;
            _previews.Clear();
            context.Assets.ClearTextureCache();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(BodySize.X - 140.0f);
        ImGui.InputTextWithHint("##filter", "Filter textures...", ref _filter, 128);

        _texturesTask ??= context.Assets.ListTextureAssetsAsync();
        if (_textures == null && _texturesTask.IsCompletedSuccessfully)
        {
            _textures = _texturesTask.Result
                .OrderBy(texture => texture.SourceName)
                .ThenBy(texture => texture.Path)
                .ToList();
        }

        DrawCurrent(context);
        ImGui.Separator();

        if (_textures == null)
        {
            ImGui.TextDisabled("Indexing textures...");
        }
        else if (_textures.Count == 0)
        {
            ImGui.TextDisabled("No texture assets found.");
        }
        else
        {
            if (DrawTextureGrid(context))
            {
                return ModalDialogState.Confirmed;
            }
        }

        ImGui.Separator();

        if (ImGui.Button("Clear", new Vector2(120, 0)))
        {
            context.Select("");
            return ModalDialogState.Confirmed;
        }

        ImGui.SameLine();

        if (ImGui.Button("Cancel", new Vector2(120, 0)))
        {
            return ModalDialogState.Cancelled;
        }

        return ModalDialogState.Running;
    }

    private static void DrawCurrent(TextureSelectionContext context)
    {
        string current = context.CurrentPath.Length == 0 ? "(none)" : context.CurrentPath;
        ImGui.TextDisabled($"Current: {current}");
    }

    private bool DrawTextureGrid(TextureSelectionContext context)
    {
        List<AssetRef> visible = FilteredTextures().ToList();
        AssetCardGrid.CardClick? click = AssetCardGrid.Draw(
            "TextureAssetGrid",
            BodySize,
            visible,
            CardWidth,
            ThumbnailSize,
            texture => texture.Path == context.CurrentPath,
            (texture, min, max) => DrawPreview(context, texture, min, max));

        if (click == null)
        {
            return false;
        }

        context.Select(click.Value.Asset.Path);
        return true;
    }

    private void DrawPreview(TextureSelectionContext context, AssetRef texture, Vector2 min, Vector2 max)
    {
        Task<Texture2D?> preview = PreviewTask(context, texture);
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        if (preview.IsCompletedSuccessfully && preview.Result != null)
        {
            draw.AddImage((IntPtr)preview.Result.GetRid().Id, min, max);
            return;
        }

        draw.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.WindowBg), 2.0f);
        string label = preview.IsCompleted ? "Failed" : "Loading";
        Vector2 textSize = ImGui.CalcTextSize(label);
        draw.AddText((min + max) * 0.5f - textSize * 0.5f, ImGui.GetColorU32(ImGuiCol.TextDisabled), label);
    }

    private Task<Texture2D?> PreviewTask(TextureSelectionContext context, AssetRef texture)
    {
        if (_previews.TryGetValue(texture.Path, out Task<Texture2D?>? preview))
        {
            return preview;
        }

        preview = context.Assets.LoadTextureAssetAsync(texture);
        _previews[texture.Path] = preview;
        return preview;
    }

    private IEnumerable<AssetRef> FilteredTextures()
    {
        if (_textures == null)
        {
            return [];
        }

        string filter = _filter.Trim();
        if (filter.Length == 0)
        {
            return _textures;
        }

        return _textures.Where(texture =>
            texture.DisplayName.Contains(filter, System.StringComparison.OrdinalIgnoreCase) ||
            texture.SourceName.Contains(filter, System.StringComparison.OrdinalIgnoreCase) ||
            texture.Path.Contains(filter, System.StringComparison.OrdinalIgnoreCase));
    }
}
