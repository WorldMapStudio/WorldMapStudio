using System.Linq;
using Godot;

namespace WorldMapStudio;

public static class PaintScriptApiTests
{
    private const int Size = 64;

    private sealed class Fixture(EditorContext context, SceneEntity entity) : IScriptModule
    {
        public string Name => "fixture";

        [ScriptFunction]
        public ScriptEntityHandle Target() => new(context.Scene, context.Catalog, context.EditSessions, entity);
    }

    private sealed class Rig
    {
        public required EditorContext Context { get; init; }
        public required PaintImage Image { get; init; }
        public required ScriptEngineHost Host { get; init; }
    }

    [EditorTest(Category = "Paint Script", Thread = TestThread.Main)]
    public static void Stroke_paints_along_the_line_only()
    {
        Rig rig = NewRig();

        Assert.AreEqual("true", rig.Host.Evaluate(Stroke("[[-20, 0], [20, 0]]")));

        byte[] pixels = rig.Image.CopyPixels();
        Assert.IsTrue(pixels[(Size / 2 * Size) + (Size / 2)] > 0, "the line's centre should be painted");
        Assert.IsTrue(pixels[0] == 0 && pixels[(Size * Size) - 1] == 0, "the corners are far from the line");
    }

    [EditorTest(Category = "Paint Script", Thread = TestThread.Main)]
    public static void Undo_restores_the_pixels_in_one_step()
    {
        Rig rig = NewRig();
        rig.Host.Evaluate(Stroke("[[-20, 0], [-10, 0], [0, 0], [10, 0], [20, 0]]"));
        Assert.IsTrue(rig.Image.CopyPixels().Any(pixel => pixel > 0));

        rig.Context.EditSessions.Undo();

        Assert.IsTrue(rig.Image.CopyPixels().All(pixel => pixel == 0), "one undo reverts the whole stroke");
    }

    [EditorTest(Category = "Paint Script", Thread = TestThread.Main)]
    public static void Erase_reduces_the_paint()
    {
        Rig rig = NewRig();
        rig.Host.Evaluate("wms.paint.SetBrush({ Opacity: 1 });" + Stroke("[[-20, 0], [20, 0]]"));
        int painted = rig.Image.CopyPixels().Sum(pixel => pixel);

        rig.Host.Evaluate(Stroke("[[-20, 0], [20, 0]]", "{ Erase: true }"));

        Assert.IsTrue(rig.Image.CopyPixels().Sum(pixel => pixel) < painted);
    }

    [EditorTest(Category = "Paint Script", Thread = TestThread.Main)]
    public static void Points_outside_the_footprint_are_skipped()
    {
        Rig rig = NewRig();

        Assert.AreEqual("false", rig.Host.Evaluate(Stroke("[[500, 500], [600, 500]]")));
        Assert.IsTrue(rig.Image.CopyPixels().All(pixel => pixel == 0));
        Assert.IsFalse(rig.Context.EditSessions.Active.IsDirty);
    }

    [EditorTest(Category = "Paint Script", Thread = TestThread.Main)]
    public static void Brush_settings_survive_a_tool_switch()
    {
        Rig rig = NewRig();
        rig.Host.Evaluate("wms.paint.SetBrush({ Radius: 7, Erase: true })");

        ToolSystem tools = rig.Context.Tools;
        tools.Activate(tools.Factories.First(factory => factory is not PaintToolFactory));
        tools.Activate(tools.PaintToolFactory);

        Assert.AreEqual("7", rig.Host.Evaluate("wms.paint.CurrentBrush.Radius.toString()"));
        Assert.AreEqual("true", rig.Host.Evaluate("wms.paint.CurrentBrush.Erase.toString()"));
    }

    [EditorTest(Category = "Paint Script", Thread = TestThread.Main)]
    public static void Stroke_options_do_not_change_the_brush()
    {
        Rig rig = NewRig();
        rig.Host.Evaluate(Stroke("[[-20, 0], [20, 0]]", "{ Radius: 2 }"));

        Assert.AreEqual("4", rig.Host.Evaluate("wms.paint.CurrentBrush.Radius.toString()"));
    }

    [EditorTest(Category = "Paint Script", Thread = TestThread.Main)]
    public static void A_reference_stroke_matches_dabs_laid_at_the_legacy_spacing()
    {
        Rig rig = NewRig();
        rig.Host.Evaluate(Stroke("[[-20, 0], [20, 0]]", "{ Radius: 4, Opacity: 0.35 }"));

        var expected = new PaintImage();
        expected.ConfigureNew(Size, Size, chunkSize: 16);
        float radius = 4.0f / Size;
        for (int i = 0; i <= 40; i++)
        {
            expected.Paint(((-20.0f + i) / Size) + 0.5f, 0.5f, radius, radius, 0.35f, erase: false);
        }

        Assert.IsTrue(rig.Image.CopyPixels().SequenceEqual(expected.CopyPixels()),
            "a stroke should lay a dab at the start and one per quarter radius of travel");
    }

    [EditorTest(Category = "Paint Script", Thread = TestThread.Main)]
    public static void Hardness_keeps_full_weight_inside_the_inner_radius()
    {
        Rig rig = NewRig();
        rig.Host.Evaluate($"wms.paint.Dab(wms.fixture.Target(), 0, 0, {{ Radius: 10, Strength: 0.5, Hardness: 1 }})");

        byte[] pixels = rig.Image.CopyPixels();
        byte centre = pixels[(32 * Size) + 32];
        Assert.IsTrue(centre > 0);
        Assert.AreEqual(centre, pixels[(32 * Size) + 40], "a hard brush is flat out to its rim");
        Assert.AreEqual((byte)0, pixels[(32 * Size) + 44]);

        Rig soft = NewRig();
        soft.Host.Evaluate($"wms.paint.Dab(wms.fixture.Target(), 0, 0, {{ Radius: 10, Strength: 0.5 }})");
        Assert.IsTrue(soft.Image.CopyPixels()[(32 * Size) + 40] < centre);
    }

    [EditorTest(Category = "Paint Script", Thread = TestThread.Main)]
    public static void Legacy_option_names_still_apply_to_the_shared_brush()
    {
        Rig rig = NewRig();
        rig.Host.Evaluate("wms.paint.SetBrush({ Opacity: 0.6, Erase: true })");

        Assert.AreEqual("0.6", rig.Host.Evaluate("wms.paint.CurrentBrush.Strength.toString()").Substring(0, 3));
        Assert.AreEqual("true", rig.Host.Evaluate("wms.paint.CurrentBrush.Invert.toString()"));
        Assert.AreEqual("true", rig.Host.Evaluate("wms.paint.CurrentBrush.Erase.toString()"));
    }

    private static string Stroke(string points, string? options = null) =>
        $"wms.paint.Stroke(wms.fixture.Target(), {points}{(options == null ? "" : ", " + options)}).toString()";

    private static Rig NewRig()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_paint_script_test__" });
        var image = new PaintImage { RecordId = 1, Name = "Image 1" };
        image.ConfigureNew(Size, Size, chunkSize: 16);
        context.Catalog.Add(image);

        var entity = new MapSceneEntity();
        entity.AddComponent(new ImageComponent(context.Images) { ImageId = image.RecordId, WorldSizeX = Size, WorldSizeZ = Size });
        context.Scene.Add(entity);

        var host = new ScriptEngineHost([context.Scripting.PaintScriptApi, new Fixture(context, entity)]);
        return new Rig { Context = context, Image = image, Host = host };
    }
}
