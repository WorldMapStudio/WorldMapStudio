using System;
using System.Collections.Generic;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// Culled, clickable grid of <see cref="AssetRef"/> cards - the layout <see cref="TextureSelectionDialog"/>
/// pioneered, shared so a second picker (models) doesn't grow its own copy. Only lays out cards and
/// reports clicks; a caller supplies how each thumbnail is drawn since that differs per asset kind.
/// </summary>
public static class AssetCardGrid
{
    public const float LabelHeight = 44.0f;
    public const float Padding = 6.0f;

    public readonly record struct CardClick(AssetRef Asset, bool DoubleClick);

    public delegate void ThumbnailDrawer(AssetRef asset, Vector2 min, Vector2 max);

    /// <summary>Draws one scrollable child window of cards, culled to the visible rows. Returns the
    /// clicked card, if any, for this frame. <paramref name="thumbnailDrawer"/> is invoked once per
    /// visible card per frame - callers that need to know which assets are on screen (to request a
    /// thumbnail load, say) can piggyback on that instead of tracking visibility themselves.</summary>
    public static CardClick? Draw(
        string childId,
        Vector2 bodySize,
        IReadOnlyList<AssetRef> items,
        float cardWidth,
        float thumbnailSize,
        Func<AssetRef, bool> isSelected,
        ThumbnailDrawer thumbnailDrawer)
    {
        CardClick? result = null;

        ImGui.BeginChild(childId, bodySize, true, ImGuiWindowFlags.None);
        if (items.Count == 0)
        {
            ImGui.TextDisabled("Nothing matches the filter.");
            ImGui.EndChild();
            return null;
        }

        float available = ImGui.GetContentRegionAvail().X;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float cardHeight = thumbnailSize + LabelHeight + Padding * 2.0f;
        float rowStep = cardHeight + spacing;
        int columns = Math.Max(1, (int)((available + spacing) / (cardWidth + spacing)));
        int rows = (items.Count + columns - 1) / columns;

        float startY = ImGui.GetCursorPosY();
        float scrollY = ImGui.GetScrollY();
        float windowHeight = ImGui.GetWindowHeight();
        int firstRow = Math.Clamp((int)(scrollY / rowStep), 0, rows - 1);
        int lastRow = Math.Clamp((int)((scrollY + windowHeight) / rowStep) + 1, firstRow, rows - 1);

        ImGui.SetCursorPosY(startY + firstRow * rowStep);
        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int index = row * columns + column;
                if (index >= items.Count)
                {
                    break;
                }

                if (column > 0)
                {
                    ImGui.SameLine();
                }

                AssetRef asset = items[index];
                CardClick? click = DrawCard(asset, cardWidth, thumbnailSize, isSelected(asset), thumbnailDrawer);
                if (click.HasValue)
                {
                    result = click;
                }
            }
        }

        ImGui.SetCursorPosY(startY + rows * rowStep);
        ImGui.EndChild();
        return result;
    }

    private static CardClick? DrawCard(AssetRef asset, float cardWidth, float thumbnailSize, bool selected, ThumbnailDrawer thumbnailDrawer)
    {
        var size = new Vector2(cardWidth, thumbnailSize + LabelHeight + Padding * 2.0f);
        Vector2 origin = ImGui.GetCursorScreenPos();

        ImGui.PushID(asset.Path);
        bool clicked = ImGui.InvisibleButton("##card", size);
        bool hovered = ImGui.IsItemHovered();
        bool doubleClicked = hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);

        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, ImGui.GetColorU32(hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg), 4.0f);

        float thumbnailX = origin.X + (cardWidth - thumbnailSize) * 0.5f;
        Vector2 thumbnailMin = new(thumbnailX, origin.Y + Padding);
        Vector2 thumbnailMax = thumbnailMin + new Vector2(thumbnailSize, thumbnailSize);
        thumbnailDrawer(asset, thumbnailMin, thumbnailMax);

        draw.AddRect(
            origin,
            origin + size,
            ImGui.GetColorU32(selected ? ImGuiCol.ButtonActive : ImGuiCol.Border),
            4.0f,
            ImDrawFlags.None,
            selected ? 2.0f : 1.0f);

        Vector2 labelMin = new(origin.X + Padding, thumbnailMax.Y + Padding);
        Vector2 labelMax = new(origin.X + cardWidth - Padding, origin.Y + size.Y);
        draw.PushClipRect(labelMin, labelMax, true);
        DrawCenteredText(draw, labelMin, labelMax.X, ImGui.GetColorU32(ImGuiCol.Text), asset.DisplayName);
        DrawCenteredText(
            draw,
            labelMin + new Vector2(0.0f, ImGui.GetTextLineHeight()),
            labelMax.X,
            ImGui.GetColorU32(ImGuiCol.TextDisabled),
            asset.SourceName);
        draw.PopClipRect();

        if (hovered)
        {
            ImGui.SetTooltip(asset.FullPath);
        }

        ImGui.PopID();

        if (doubleClicked)
        {
            return new CardClick(asset, true);
        }

        return clicked ? new CardClick(asset, false) : null;
    }

    private static void DrawCenteredText(ImDrawListPtr draw, Vector2 lineMin, float lineMaxX, uint color, string text)
    {
        Vector2 textSize = ImGui.CalcTextSize(text);
        float available = lineMaxX - lineMin.X;
        float x = lineMin.X + Math.Max(0.0f, (available - textSize.X) * 0.5f);
        draw.AddText(new Vector2(x, lineMin.Y), color, text);
    }
}
