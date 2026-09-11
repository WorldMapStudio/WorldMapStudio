using System.Numerics;

namespace WorldMapStudio;

/// <summary>Covers <see cref="Canvas2DView"/> (pure pan/zoom math) and
/// <see cref="Canvas2DRectHandles"/> (hit-testing and dragging).</summary>
public static class Canvas2DViewTests
{
    private static readonly Vector2 CanvasSize = new(800.0f, 600.0f);

    [EditorTest(Category = "Canvas", Thread = TestThread.Background)]
    public static void To_screen_and_to_pixel_are_inverses()
    {
        var view = new Canvas2DView(new Vector2(100.0f, -40.0f), zoom: 2.5f);

        foreach (Vector2 content in new[]
                 {
                     new Vector2(100.0f, -40.0f), new Vector2(0.0f, 0.0f), new Vector2(-500.0f, 300.0f),
                 })
        {
            Vector2 screen = view.ToScreen(content, CanvasSize);
            Vector2 roundTripped = view.ToPixel(screen, CanvasSize);
            Assert.AreApproximatelyEqual(content.X, roundTripped.X, 0.001, $"{content} round-trip X");
            Assert.AreApproximatelyEqual(content.Y, roundTripped.Y, 0.001, $"{content} round-trip Y");
        }
    }

    [EditorTest(Category = "Canvas", Thread = TestThread.Background)]
    public static void Center_is_drawn_at_the_canvas_middle()
    {
        var view = new Canvas2DView(new Vector2(37.0f, -12.0f), zoom: 3.0f);
        Vector2 screen = view.ToScreen(view.Center, CanvasSize);
        Assert.AreApproximatelyEqual(CanvasSize.X * 0.5, screen.X, 0.001);
        Assert.AreApproximatelyEqual(CanvasSize.Y * 0.5, screen.Y, 0.001);
    }

    [EditorTest(Category = "Canvas", Thread = TestThread.Background)]
    public static void Pan_by_moves_content_the_same_direction_as_the_drag()
    {
        var view = new Canvas2DView(Vector2.Zero, zoom: 2.0f);
        Vector2 contentUnderCursorBefore = view.ToPixel(new Vector2(400.0f, 300.0f), CanvasSize);

        view.PanBy(new Vector2(20.0f, -10.0f));

        Vector2 contentUnderCursorAfter = view.ToPixel(new Vector2(420.0f, 290.0f), CanvasSize);
        Assert.AreApproximatelyEqual(contentUnderCursorBefore.X, contentUnderCursorAfter.X, 0.001,
            "the point under the cursor follows the drag");
        Assert.AreApproximatelyEqual(contentUnderCursorBefore.Y, contentUnderCursorAfter.Y, 0.001);
    }

    [EditorTest(Category = "Canvas", Thread = TestThread.Background)]
    public static void Zoom_at_holds_the_anchor_point_fixed_on_screen()
    {
        var view = new Canvas2DView(new Vector2(10.0f, 5.0f), zoom: 1.0f);
        var anchorScreen = new Vector2(600.0f, 200.0f);
        Vector2 anchorContentBefore = view.ToPixel(anchorScreen, CanvasSize);

        view.ZoomAt(3.0f, anchorScreen, CanvasSize, minZoom: 0.01f, maxZoom: 100.0f);

        Assert.AreApproximatelyEqual(1.0 * System.Math.Pow(1.15, 3.0), view.Zoom, 0.0001, "zoom curve");
        Vector2 screenAfter = view.ToScreen(anchorContentBefore, CanvasSize);
        Assert.AreApproximatelyEqual(anchorScreen.X, screenAfter.X, 0.01, "anchor stays under cursor X");
        Assert.AreApproximatelyEqual(anchorScreen.Y, screenAfter.Y, 0.01, "anchor stays under cursor Y");
    }

    [EditorTest(Category = "Canvas", Thread = TestThread.Background)]
    public static void Zoom_at_clamps_to_the_given_range()
    {
        var view = new Canvas2DView(Vector2.Zero, zoom: 1.0f);
        view.ZoomAt(-100.0f, CanvasSize * 0.5f, CanvasSize, minZoom: 0.5f, maxZoom: 8.0f);
        Assert.AreEqual(0.5f, view.Zoom);

        view.ZoomAt(100.0f, CanvasSize * 0.5f, CanvasSize, minZoom: 0.5f, maxZoom: 8.0f);
        Assert.AreEqual(8.0f, view.Zoom);
    }

    [EditorTest(Category = "Canvas", Thread = TestThread.Background)]
    public static void Fit_to_centres_and_scales_to_the_tighter_axis()
    {
        var view = new Canvas2DView(Vector2.Zero, zoom: 1.0f);
        var content = new CanvasRect(new Vector2(0.0f, 0.0f), new Vector2(1002.0f, 668.0f));

        view.FitTo(content, CanvasSize, margin: 1.0f);

        Assert.AreApproximatelyEqual(content.Center.X, view.Center.X, 0.01);
        Assert.AreApproximatelyEqual(content.Center.Y, view.Center.Y, 0.01);
        // 800/1002 < 600/668, so width is the binding axis.
        Assert.AreApproximatelyEqual(CanvasSize.X / content.Size.X, view.Zoom, 0.0001);
    }

    [EditorTest(Category = "Canvas", Thread = TestThread.Background)]
    public static void Clamp_keeps_at_most_half_a_canvas_past_the_bounds()
    {
        var view = new Canvas2DView(new Vector2(100_000.0f, -100_000.0f), zoom: 2.0f);
        var bounds = new CanvasRect(new Vector2(0.0f, 0.0f), new Vector2(1000.0f, 1000.0f));

        view.Clamp(bounds, CanvasSize);

        Vector2 half = (CanvasSize * 0.5f) / view.Zoom;
        Assert.AreApproximatelyEqual(bounds.Max.X + half.X, view.Center.X, 0.01, "clamped max X");
        Assert.AreApproximatelyEqual(bounds.Min.Y - half.Y, view.Center.Y, 0.01, "clamped min Y");
    }

    [EditorTest(Category = "Canvas", Thread = TestThread.Background)]
    public static void Hit_test_finds_every_corner_edge_and_the_body()
    {
        var view = new Canvas2DView(Vector2.Zero, zoom: 1.0f);
        var rect = new CanvasRect(new Vector2(-100.0f, -50.0f), new Vector2(100.0f, 50.0f));
        Vector2 topLeftScreen = view.ToScreen(rect.Min, CanvasSize);
        Vector2 bottomRightScreen = view.ToScreen(rect.Max, CanvasSize);

        Assert.AreEqual(RectHandleKind.TopLeft, Canvas2DRectHandles.HitTest(rect, view, CanvasSize, topLeftScreen, 6.0f));
        Assert.AreEqual(RectHandleKind.BottomRight, Canvas2DRectHandles.HitTest(rect, view, CanvasSize, bottomRightScreen, 6.0f));
        Assert.AreEqual(RectHandleKind.Body, Canvas2DRectHandles.HitTest(rect, view, CanvasSize, view.ToScreen(Vector2.Zero, CanvasSize), 6.0f));
        Assert.AreEqual(RectHandleKind.None, Canvas2DRectHandles.HitTest(rect, view, CanvasSize, view.ToScreen(new Vector2(1000.0f, 1000.0f), CanvasSize), 6.0f));
    }

    [EditorTest(Category = "Canvas", Thread = TestThread.Background)]
    public static void Body_drag_moves_the_whole_rect()
    {
        var rect = new CanvasRect(new Vector2(0.0f, 0.0f), new Vector2(100.0f, 50.0f));
        CanvasRect moved = Canvas2DRectHandles.Drag(rect, RectHandleKind.Body, new Vector2(10.0f, -5.0f), aspectLock: false);
        Assert.AreEqual(new CanvasRect(new Vector2(10.0f, -5.0f), new Vector2(110.0f, 45.0f)), moved);
    }

    [EditorTest(Category = "Canvas", Thread = TestThread.Background)]
    public static void Edge_drag_moves_only_its_own_edge()
    {
        var rect = new CanvasRect(new Vector2(0.0f, 0.0f), new Vector2(100.0f, 50.0f));
        CanvasRect dragged = Canvas2DRectHandles.Drag(rect, RectHandleKind.Right, new Vector2(20.0f, 0.0f), aspectLock: false);
        Assert.AreEqual(new CanvasRect(new Vector2(0.0f, 0.0f), new Vector2(120.0f, 50.0f)), dragged);
    }

    [EditorTest(Category = "Canvas", Thread = TestThread.Background)]
    public static void Corner_drag_with_aspect_lock_holds_the_ratio_anchored_at_the_opposite_corner()
    {
        // 3:2 rect (200 x 300, Y/X = 1.5), dragging the top-left corner in.
        var rect = new CanvasRect(new Vector2(0.0f, 0.0f), new Vector2(200.0f, 300.0f));
        CanvasRect dragged = Canvas2DRectHandles.Drag(rect, RectHandleKind.TopLeft, new Vector2(40.0f, 0.0f), aspectLock: true);

        Assert.AreApproximatelyEqual(1.5, dragged.Size.Y / dragged.Size.X, 0.0001, "aspect held");
        Assert.AreApproximatelyEqual(200.0, dragged.Max.X, 0.001, "opposite corner X anchored");
        Assert.AreApproximatelyEqual(300.0, dragged.Max.Y, 0.001, "opposite corner Y anchored");
        Assert.AreApproximatelyEqual(40.0, dragged.Min.X, 0.001, "dragged edge moved");
    }

    [EditorTest(Category = "Canvas", Thread = TestThread.Background)]
    public static void Edge_drag_with_aspect_lock_resizes_the_perpendicular_axis_about_the_centre()
    {
        var rect = new CanvasRect(new Vector2(0.0f, 0.0f), new Vector2(200.0f, 300.0f));
        CanvasRect dragged = Canvas2DRectHandles.Drag(rect, RectHandleKind.Right, new Vector2(100.0f, 0.0f), aspectLock: true);

        Assert.AreApproximatelyEqual(1.5, dragged.Size.Y / dragged.Size.X, 0.0001, "aspect held");
        Assert.AreApproximatelyEqual(150.0, dragged.Center.Y, 0.001, "vertical centre unchanged");
        Assert.AreApproximatelyEqual(300.0, dragged.Max.X, 0.001, "dragged edge moved");
    }

    [EditorTest(Category = "Canvas", Thread = TestThread.Background)]
    public static void No_handle_leaves_the_rect_unchanged()
    {
        var rect = new CanvasRect(new Vector2(0.0f, 0.0f), new Vector2(100.0f, 50.0f));
        CanvasRect result = Canvas2DRectHandles.Drag(rect, RectHandleKind.None, new Vector2(999.0f, 999.0f), aspectLock: false);
        Assert.AreEqual(rect, result);
    }
}
