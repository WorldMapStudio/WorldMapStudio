using System;
using System.Numerics;

namespace WorldMapStudio;

/// <summary>An axis-aligned rect in content space (whatever unit the caller works in). Deliberately
/// distinct from any Godot rect type — <see cref="Canvas2DView"/>'s whole point is having no Godot or
/// ImGui dependency.</summary>
public readonly record struct CanvasRect(Vector2 Min, Vector2 Max)
{
    public Vector2 Size => Max - Min;
    public Vector2 Center => (Min + Max) * 0.5f;

    public static CanvasRect FromCenterSize(Vector2 center, Vector2 size) =>
        new(center - (size * 0.5f), center + (size * 0.5f));
}

/// <summary>
/// Pan/zoom state and math for a 2D content canvas — "content" is whatever unit the caller works in
/// (image pixels for the paint canvas, world-map page pixels for the world map editor). No ImGui or
/// Godot types, so it's testable in-editor and reusable by any 2D canvas window; callers own input
/// handling and drawing on top of this. See <see cref="Canvas2DRectHandles"/> for the companion
/// rect-drag helper.
/// </summary>
public sealed class Canvas2DView
{
    /// <summary>The content-space point currently drawn at the centre of the canvas.</summary>
    public Vector2 Center { get; set; }

    /// <summary>Screen pixels per content unit.</summary>
    public float Zoom { get; set; }

    public Canvas2DView(Vector2 center, float zoom = 1.0f)
    {
        Center = center;
        Zoom = zoom;
    }

    /// <summary>Content point → screen point, relative to the canvas's own top-left corner.</summary>
    public Vector2 ToScreen(Vector2 content, Vector2 canvasSize) =>
        ((content - Center) * Zoom) + (canvasSize * 0.5f);

    /// <summary>The inverse of <see cref="ToScreen"/>.</summary>
    public Vector2 ToPixel(Vector2 screen, Vector2 canvasSize) =>
        ((screen - (canvasSize * 0.5f)) / Zoom) + Center;

    /// <summary>Pans by a screen-space drag delta.</summary>
    public void PanBy(Vector2 screenDelta) => Center -= screenDelta / Zoom;

    /// <summary>Zooms by <paramref name="wheel"/> ticks (positive = in), keeping the content point
    /// under <paramref name="anchorScreen"/> fixed on screen. Exponential (<c>1.15^wheel</c>),
    /// matching <c>FlyCamera</c>'s own wheel curve.</summary>
    public void ZoomAt(float wheel, Vector2 anchorScreen, Vector2 canvasSize, float minZoom, float maxZoom)
    {
        if (wheel == 0.0f)
        {
            return;
        }

        Vector2 anchorContent = ToPixel(anchorScreen, canvasSize);
        Zoom = Math.Clamp(Zoom * MathF.Pow(1.15f, wheel), minZoom, maxZoom);
        Center = anchorContent - ((anchorScreen - (canvasSize * 0.5f)) / Zoom);
    }

    /// <summary>Centres on and zooms to fit <paramref name="content"/> inside <paramref
    /// name="canvasSize"/>, leaving an even margin (1.0 = exact fit, &lt;1.0 = extra breathing room).</summary>
    public void FitTo(CanvasRect content, Vector2 canvasSize, float margin = 0.9f)
    {
        if (content.Size.X <= 0.0f || content.Size.Y <= 0.0f || canvasSize.X <= 0.0f || canvasSize.Y <= 0.0f)
        {
            return;
        }

        Zoom = MathF.Min(canvasSize.X / content.Size.X, canvasSize.Y / content.Size.Y) * margin;
        Center = content.Center;
    }

    /// <summary>Keeps <see cref="Center"/> from drifting more than half a canvas past <paramref
    /// name="contentBounds"/>'s edges, so the content can never scroll fully off-screen.</summary>
    public void Clamp(CanvasRect contentBounds, Vector2 canvasSize)
    {
        Vector2 halfCanvasContent = (canvasSize * 0.5f) / Zoom;
        Center = new Vector2(
            Math.Clamp(Center.X, contentBounds.Min.X - halfCanvasContent.X, contentBounds.Max.X + halfCanvasContent.X),
            Math.Clamp(Center.Y, contentBounds.Min.Y - halfCanvasContent.Y, contentBounds.Max.Y + halfCanvasContent.Y));
    }
}
