using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

public sealed class TextureSelectionOperation : IModalOperation<TextureSelectionContext>
{
    private static readonly Vector2 BodySize = new(768, 432);

    private const float CardWidth = 160.0f;
    private const float ThumbnailSize = 128.0f;
    private const float LabelHeight = 44.0f;
    private const float Padding = 6.0f;

    private string _filter = "";
    private List<AssetRef>? _textures;
    private readonly Dictionary<string, Task<Texture2D?>> _previews = new();

    public ModalOperationState Draw(TextureSelectionContext context)
    {
        ImGui.Text("Select Texture");
        ImGui.Separator();

        if (ImGui.Button("Refresh"))
        {
            _textures = null;
            _previews.Clear();
            context.Assets.ClearTextureCache();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(BodySize.X - 140.0f);
        ImGui.InputTextWithHint("##filter", "Filter textures...", ref _filter, 128);

        _textures ??= context.Assets.ListTextureAssets()
            .OrderBy(texture => texture.SourceName)
            .ThenBy(texture => texture.Path)
            .ToList();

        DrawCurrent(context);
        ImGui.Separator();

        if (_textures.Count == 0)
        {
            ImGui.TextDisabled("No texture assets found.");
        }
        else
        {
            if (DrawTextureGrid(context))
            {
                return ModalOperationState.Confirmed;
            }
        }

        ImGui.Separator();

        if (ImGui.Button("Clear", new Vector2(120, 0)))
        {
            context.Select("");
            return ModalOperationState.Confirmed;
        }

        ImGui.SameLine();

        if (ImGui.Button("Cancel", new Vector2(120, 0)))
        {
            return ModalOperationState.Cancelled;
        }

        return ModalOperationState.Running;
    }

    private static void DrawCurrent(TextureSelectionContext context)
    {
        string current = context.CurrentPath.Length == 0 ? "(none)" : context.CurrentPath;
        ImGui.TextDisabled($"Current: {current}");
    }

    private bool DrawTextureGrid(TextureSelectionContext context)
    {
        List<AssetRef> visible = FilteredTextures().ToList();
        bool clicked = false;

        ImGui.BeginChild("TextureAssetGrid", BodySize, true, ImGuiWindowFlags.None);
        if (visible.Count == 0)
        {
            ImGui.TextDisabled("No textures match the filter.");
            ImGui.EndChild();
            return false;
        }

        float available = ImGui.GetContentRegionAvail().X;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float cardHeight = ThumbnailSize + LabelHeight + Padding * 2.0f;
        float rowStep = cardHeight + spacing;
        int columns = System.Math.Max(1, (int)((available + spacing) / (CardWidth + spacing)));
        int rows = (visible.Count + columns - 1) / columns;

        float startY = ImGui.GetCursorPosY();
        float scrollY = ImGui.GetScrollY();
        float windowHeight = ImGui.GetWindowHeight();
        int firstRow = System.Math.Clamp((int)(scrollY / rowStep), 0, rows - 1);
        int lastRow = System.Math.Clamp((int)((scrollY + windowHeight) / rowStep) + 1, firstRow, rows - 1);

        ImGui.SetCursorPosY(startY + firstRow * rowStep);
        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int index = row * columns + column;
                if (index >= visible.Count)
                {
                    break;
                }

                if (column > 0)
                {
                    ImGui.SameLine();
                }

                clicked |= DrawTextureCard(context, visible[index]);
            }
        }

        ImGui.SetCursorPosY(startY + rows * rowStep);
        ImGui.EndChild();
        return clicked;
    }

    private bool DrawTextureCard(TextureSelectionContext context, AssetRef texture)
    {
        var size = new Vector2(CardWidth, ThumbnailSize + LabelHeight + Padding * 2.0f);
        Vector2 origin = ImGui.GetCursorScreenPos();

        ImGui.PushID(texture.Path);
        bool clicked = ImGui.InvisibleButton("##texture", size);
        bool hovered = ImGui.IsItemHovered();
        bool selected = texture.Path == context.CurrentPath;

        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, ImGui.GetColorU32(hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg), 4.0f);

        float thumbnailX = origin.X + (CardWidth - ThumbnailSize) * 0.5f;
        Vector2 thumbnailMin = new(thumbnailX, origin.Y + Padding);
        Vector2 thumbnailMax = thumbnailMin + new Vector2(ThumbnailSize, ThumbnailSize);
        DrawPreview(context, texture, thumbnailMin, thumbnailMax);

        draw.AddRect(
            origin,
            origin + size,
            ImGui.GetColorU32(selected ? ImGuiCol.ButtonActive : ImGuiCol.Border),
            4.0f,
            ImDrawFlags.None,
            selected ? 2.0f : 1.0f);

        Vector2 labelMin = new(origin.X + Padding, thumbnailMax.Y + Padding);
        Vector2 labelMax = new(origin.X + CardWidth - Padding, origin.Y + size.Y);
        draw.PushClipRect(labelMin, labelMax, true);
        DrawCenteredText(draw, labelMin, labelMax.X, ImGui.GetColorU32(ImGuiCol.Text), texture.DisplayName);
        DrawCenteredText(
            draw,
            labelMin + new Vector2(0.0f, ImGui.GetTextLineHeight()),
            labelMax.X,
            ImGui.GetColorU32(ImGuiCol.TextDisabled),
            texture.SourceName);
        draw.PopClipRect();

        if (hovered)
        {
            ImGui.SetTooltip(texture.FullPath);
        }

        ImGui.PopID();

        if (!clicked)
        {
            return false;
        }

        context.Select(texture.Path);
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
        string label = preview.IsFaulted ? "Failed" : "Loading";
        Vector2 textSize = ImGui.CalcTextSize(label);
        draw.AddText((min + max) * 0.5f - textSize * 0.5f, ImGui.GetColorU32(ImGuiCol.TextDisabled), label);
    }

    private static void DrawCenteredText(ImDrawListPtr draw, Vector2 lineMin, float lineMaxX, uint color, string text)
    {
        Vector2 textSize = ImGui.CalcTextSize(text);
        float available = lineMaxX - lineMin.X;
        float x = lineMin.X + System.Math.Max(0.0f, (available - textSize.X) * 0.5f);
        draw.AddText(new Vector2(x, lineMin.Y), color, text);
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
