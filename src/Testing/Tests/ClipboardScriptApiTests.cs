using System.Linq;
using Godot;

namespace WorldMapStudio;

public static class ClipboardScriptApiTests
{
    private sealed class Fixture(EditorContext context, SceneEntity[] entities) : IScriptModule
    {
        public string Name => "fixture";

        [ScriptFunction]
        public ScriptEntityHandle[] All() =>
            entities.Select(entity => new ScriptEntityHandle(context.Scene, context.Catalog, context.EditSessions, entity)).ToArray();
    }

    [EditorTest(Category = "Clipboard Script", Thread = TestThread.Main)]
    public static void Paste_lands_the_bottom_centre_at_the_point_keeps_the_layout_and_undoes()
    {
        (EditorContext context, ScriptEngineHost host) = NewRig(gap: 10.0f);
        int before = context.Scene.Entities.Count();

        host.Evaluate("wms.clipboard.Copy(wms.fixture.All())");
        Assert.AreEqual("true", host.Evaluate("wms.clipboard.HasContent.toString()"));
        host.Evaluate("var pasted = wms.clipboard.Paste(100, 0, 50)");

        Assert.AreEqual(before + 2, context.Scene.Entities.Count());

        SceneEntity[] pasted = context.Selection.Selected.OfType<SceneEntity>().OrderBy(entity => entity.Transform.Origin.X).ToArray();
        Assert.AreEqual(2, pasted.Length);
        Aabb bounds = pasted[0].WorldBounds.Merge(pasted[1].WorldBounds);
        Assert.AreApproximatelyEqual(100.0, bounds.Position.X + (bounds.Size.X * 0.5f), 1e-3, "bottom-centre x");
        Assert.AreApproximatelyEqual(0.0, bounds.Position.Y, 1e-3, "bottom y");
        Assert.AreApproximatelyEqual(50.0, bounds.Position.Z + (bounds.Size.Z * 0.5f), 1e-3, "bottom-centre z");
        Assert.AreApproximatelyEqual(10.0, pasted[1].Transform.Origin.X - pasted[0].Transform.Origin.X, 1e-3, "the layout is preserved");

        context.EditSessions.Undo();
        Assert.AreEqual(before, context.Scene.Entities.Count());
    }

    [EditorTest(Category = "Clipboard Script", Thread = TestThread.Main)]
    public static void Pasting_two_close_entities_keeps_their_offset()
    {
        (EditorContext context, ScriptEngineHost host) = NewRig(gap: 2.0f);

        host.Evaluate("wms.clipboard.Copy(wms.fixture.All())");
        host.Evaluate("var pasted = wms.clipboard.Paste(100, 0, 50)");

        double firstX = double.Parse(host.Evaluate("wms.scene.GetPosition(pasted[0])[0].toString()"), System.Globalization.CultureInfo.InvariantCulture);
        double secondX = double.Parse(host.Evaluate("wms.scene.GetPosition(pasted[1])[0].toString()"), System.Globalization.CultureInfo.InvariantCulture);
        Assert.AreApproximatelyEqual(2.0, secondX - firstX, 1e-4, "the second entity stays where it was relative to the first");
    }

    [EditorTest(Category = "Clipboard Script", Thread = TestThread.Main)]
    public static void Clear_and_an_empty_paste()
    {
        (EditorContext context, ScriptEngineHost host) = NewRig(gap: 10.0f);
        host.Evaluate("wms.clipboard.Copy(wms.fixture.All())");

        host.Evaluate("wms.clipboard.Clear()");

        Assert.AreEqual("false", host.Evaluate("wms.clipboard.HasContent.toString()"));
        Assert.AreEqual("0", host.Evaluate("wms.clipboard.Paste(0, 0, 0).length.toString()"));
    }

    [EditorTest(Category = "Clipboard Script", Thread = TestThread.Main)]
    public static void Copy_defaults_to_the_selection()
    {
        (EditorContext context, ScriptEngineHost host) = NewRig(gap: 10.0f);
        context.Selection.Set(context.Scene.Entities.First());

        host.Evaluate("wms.clipboard.Copy()");

        Assert.AreEqual("1", host.Evaluate("wms.clipboard.Paste(0, 0, 0, false).length.toString()"));
    }

    private static (EditorContext, ScriptEngineHost) NewRig(float gap)
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_clipboard_script_test__" });
        var first = new MapSceneEntity { Name = "A", Map = context.Maps.CurrentMap };
        var second = new MapSceneEntity { Name = "B", Map = context.Maps.CurrentMap };
        second.Transform = new Transform3D(Basis.Identity, new Vector3(gap, 0, 0));

        context.Scene.Add(first);
        context.Scene.Add(second);

        var host = new ScriptEngineHost([context.Scripting.ClipboardScriptApi, context.Scripting.SceneScriptApi, new Fixture(context, [first, second])]);
        return (context, host);
    }
}
