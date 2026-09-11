using System;
using System.Numerics;

namespace WorldMapStudio;

/// <summary>Which part of a <see cref="Canvas2DRectHandles"/> rect a screen point landed on.</summary>
public enum RectHandleKind
{
    None,
    Body,
    Left,
    Right,
    Top,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// Drag-body/edges/corners hit-testing and dragging for a rect drawn through a <see cref="Canvas2DView"/>
/// — the tool behind "Page bounds", "Overlay hit rect", "Floor bounds" and "Transform region" in the
/// world map editor, and any future canvas window that edits a rect in content space. Pure logic: the
/// caller owns the mouse-down/drag/up loop and the drawing, and passes this the points and deltas.
/// </summary>
public static class Canvas2DRectHandles
{
    /// <summary>Which handle (if any) <paramref name="screenPoint"/> is over, within
    /// <paramref name="handleRadiusPx"/> screen pixels of an edge/corner. Body wins when the point is
    /// inside the rect but not near any edge.</summary>
    public static RectHandleKind HitTest(CanvasRect rect, Canvas2DView view, Vector2 canvasSize,
        Vector2 screenPoint, float handleRadiusPx)
    {
        Vector2 a = view.ToScreen(rect.Min, canvasSize);
        Vector2 b = view.ToScreen(rect.Max, canvasSize);
        float left = MathF.Min(a.X, b.X);
        float right = MathF.Max(a.X, b.X);
        float top = MathF.Min(a.Y, b.Y);
        float bottom = MathF.Max(a.Y, b.Y);

        bool nearLeft = MathF.Abs(screenPoint.X - left) <= handleRadiusPx;
        bool nearRight = MathF.Abs(screenPoint.X - right) <= handleRadiusPx;
        bool nearTop = MathF.Abs(screenPoint.Y - top) <= handleRadiusPx;
        bool nearBottom = MathF.Abs(screenPoint.Y - bottom) <= handleRadiusPx;
        bool withinX = screenPoint.X >= left - handleRadiusPx && screenPoint.X <= right + handleRadiusPx;
        bool withinY = screenPoint.Y >= top - handleRadiusPx && screenPoint.Y <= bottom + handleRadiusPx;

        if (nearLeft && nearTop) return RectHandleKind.TopLeft;
        if (nearRight && nearTop) return RectHandleKind.TopRight;
        if (nearLeft && nearBottom) return RectHandleKind.BottomLeft;
        if (nearRight && nearBottom) return RectHandleKind.BottomRight;
        if (nearLeft && withinY) return RectHandleKind.Left;
        if (nearRight && withinY) return RectHandleKind.Right;
        if (nearTop && withinX) return RectHandleKind.Top;
        if (nearBottom && withinX) return RectHandleKind.Bottom;
        if (screenPoint.X >= left && screenPoint.X <= right && screenPoint.Y >= top && screenPoint.Y <= bottom)
        {
            return RectHandleKind.Body;
        }

        return RectHandleKind.None;
    }

    /// <summary>Applies a content-space drag delta to <paramref name="rect"/> for <paramref
    /// name="handle"/>. Body moves the whole rect; an edge or corner moves only the edges it owns.
    /// With <paramref name="aspectLock"/> set, a corner drag holds the rect's aspect ratio anchored at
    /// the opposite corner, and an edge drag holds it by growing/shrinking the perpendicular axis
    /// evenly about the centre — what "Page bounds (3:2 locked)" drags on.</summary>
    public static CanvasRect Drag(CanvasRect rect, RectHandleKind handle, Vector2 contentDelta, bool aspectLock)
    {
        if (handle == RectHandleKind.None)
        {
            return rect;
        }

        if (handle == RectHandleKind.Body)
        {
            return new CanvasRect(rect.Min + contentDelta, rect.Max + contentDelta);
        }

        bool touchesLeft = handle is RectHandleKind.Left or RectHandleKind.TopLeft or RectHandleKind.BottomLeft;
        bool touchesRight = handle is RectHandleKind.Right or RectHandleKind.TopRight or RectHandleKind.BottomRight;
        bool touchesTop = handle is RectHandleKind.Top or RectHandleKind.TopLeft or RectHandleKind.TopRight;
        bool touchesBottom = handle is RectHandleKind.Bottom or RectHandleKind.BottomLeft or RectHandleKind.BottomRight;

        float minX = rect.Min.X + (touchesLeft ? contentDelta.X : 0.0f);
        float maxX = rect.Max.X + (touchesRight ? contentDelta.X : 0.0f);
        float minY = rect.Min.Y + (touchesTop ? contentDelta.Y : 0.0f);
        float maxY = rect.Max.Y + (touchesBottom ? contentDelta.Y : 0.0f);
        var dragged = new CanvasRect(new Vector2(minX, minY), new Vector2(maxX, maxY));

        if (!aspectLock || rect.Size.X == 0.0f || rect.Size.Y == 0.0f)
        {
            return dragged;
        }

        float aspect = rect.Size.Y / rect.Size.X;
        bool isCorner = handle is RectHandleKind.TopLeft or RectHandleKind.TopRight
            or RectHandleKind.BottomLeft or RectHandleKind.BottomRight;

        if (isCorner)
        {
            float anchorY = touchesTop ? dragged.Max.Y : dragged.Min.Y;
            float width = MathF.Abs(dragged.Size.X);
            float height = width * aspect;
            float draggedY = touchesTop ? anchorY - height : anchorY + height;
            return new CanvasRect(
                new Vector2(dragged.Min.X, MathF.Min(anchorY, draggedY)),
                new Vector2(dragged.Max.X, MathF.Max(anchorY, draggedY)));
        }

        Vector2 center = dragged.Center;
        if (touchesLeft || touchesRight)
        {
            float width = MathF.Abs(dragged.Size.X);
            float height = width * aspect;
            return new CanvasRect(
                new Vector2(dragged.Min.X, center.Y - (height * 0.5f)),
                new Vector2(dragged.Max.X, center.Y + (height * 0.5f)));
        }

        float fixedHeight = MathF.Abs(dragged.Size.Y);
        float fixedWidth = fixedHeight / aspect;
        return new CanvasRect(
            new Vector2(center.X - (fixedWidth * 0.5f), dragged.Min.Y),
            new Vector2(center.X + (fixedWidth * 0.5f), dragged.Max.Y));
    }
}
