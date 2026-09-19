using System.Linq;
using Godot;

namespace WorldMapStudio;

public static class ImagesScriptApiTests
{
    private const int Size = 64;

    private sealed class Fixture(EditorContext context) : IScriptModule
    {
        public SceneEntity? Entity { get; set; }

        public string Name => "fixture";

        [ScriptFunction]
        public ScriptEntityHandle Target() => new(context.Scene, context.Catalog, context.EditSessions, Entity!);
    }

    [EditorTest(Category = "Images Script", Thread = TestThread.Main)]
    public static void Create_paint_sample_undo_and_delete()
    {
        (EditorContext context, ScriptEngineHost host, Fixture fixture) = NewRig();

        int id = int.Parse(host.Evaluate($"wms.images.Create({{ name: 'Mask', width: {Size}, height: {Size}, chunkSize: 16 }}).Id.toString()"));
        Assert.AreEqual("Mask", host.Evaluate("wms.images.List().filter(i => i.Name == 'Mask')[0].Name"));
        Assert.AreEqual("Database", host.Evaluate("wms.images.List().filter(i => i.Name == 'Mask')[0].StorageKind"));

        var entity = new SceneEntity();
        entity.AddComponent(new ImageComponent(context.Images) { ImageId = id, WorldSizeX = Size, WorldSizeZ = Size });
        context.Scene.Add(entity);
        fixture.Entity = entity;

        host.Evaluate("wms.paint.SetBrush({ Opacity: 1 }); wms.paint.Stroke(wms.fixture.Target(), [[-20, 0], [20, 0]])");
        Assert.AreEqual("true", host.Evaluate($"(wms.images.Sample({id}, 0.5, 0.5) > 0).toString()"));
        Assert.AreEqual("true", host.Evaluate($"(wms.images.Sample({id}, 0.02, 0.02) == 0).toString()"));
        Assert.AreEqual("1", host.Evaluate($"wms.images.List().filter(i => i.Id == {id})[0].UsageCount.toString()"));

        context.EditSessions.Undo();
        Assert.AreEqual("0", host.Evaluate($"wms.images.Sample({id}, 0.5, 0.5).toString()"));

        string refusal = host.Evaluate($"try {{ wms.images.Delete({id}); 'deleted' }} catch (e) {{ e.message }}");
        Assert.IsTrue(refusal.Contains("in use by 1 entities"), $"deleting an in-use image is refused: {refusal}");

        context.Scene.Remove(entity);
        host.Evaluate($"wms.images.Delete({id})");
        Assert.AreEqual("0", host.Evaluate("wms.images.List().filter(i => i.Name == 'Mask').length.toString()"));

        context.EditSessions.Undo();
        Assert.AreEqual("1", host.Evaluate("wms.images.List().filter(i => i.Name == 'Mask').length.toString()"));
    }

    [EditorTest(Category = "Images Script", Thread = TestThread.Main)]
    public static void Duplicate_copies_the_pixels_and_undoes()
    {
        (EditorContext context, ScriptEngineHost host, Fixture fixture) = NewRig();
        int id = int.Parse(host.Evaluate($"wms.images.Create({{ name: 'Source', width: {Size}, height: {Size}, chunkSize: 16 }}).Id.toString()"));

        var entity = new SceneEntity();
        entity.AddComponent(new ImageComponent(context.Images) { ImageId = id, WorldSizeX = Size, WorldSizeZ = Size });
        context.Scene.Add(entity);
        fixture.Entity = entity;
        host.Evaluate("wms.paint.SetBrush({ Opacity: 1 }); wms.paint.Stroke(wms.fixture.Target(), [[-20, 0], [20, 0]])");

        int copy = int.Parse(host.Evaluate($"wms.images.Duplicate({id}).Id.toString()"));

        Assert.AreNotEqual(id, copy);
        Assert.AreEqual(
            host.Evaluate($"wms.images.Sample({id}, 0.5, 0.5).toString()"),
            host.Evaluate($"wms.images.Sample({copy}, 0.5, 0.5).toString()"));

        context.EditSessions.Undo();
        Assert.IsNull(context.Images.FindImage(copy));
    }

    [EditorTest(Category = "Images Script", Thread = TestThread.Main)]
    public static void Create_rejects_bad_components()
    {
        (_, ScriptEngineHost host, _) = NewRig();
        string before = host.Evaluate("wms.images.List().length.toString()");

        string error = host.Evaluate("try { wms.images.Create({ width: 16, height: 16, chunkSize: 16, components: 2 }); 'created' } catch (e) { e.message }");

        Assert.IsTrue(error.Contains("components"), error);
        Assert.AreEqual(before, host.Evaluate("wms.images.List().length.toString()"));
    }

    [EditorTest(Category = "Images Script", Thread = TestThread.Main)]
    public static void Layers_are_listed_with_their_usage()
    {
        (EditorContext context, ScriptEngineHost host, _) = NewRig();
        context.Catalog.Add(new ImageDisplayLayer { RecordId = 1, Name = "Overlay" });

        Assert.AreEqual("Overlay", host.Evaluate("wms.images.Layers().filter(l => l.Id == 1)[0].Name"));
        Assert.AreEqual("0", host.Evaluate("wms.images.Layers().filter(l => l.Id == 1)[0].UsageCount.toString()"));
    }

    private static (EditorContext, ScriptEngineHost, Fixture) NewRig()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_images_script_test__" });
        var fixture = new Fixture(context);
        var host = new ScriptEngineHost([context.Scripting.ImagesScriptApi, context.Scripting.PaintScriptApi, fixture]);
        return (context, host, fixture);
    }
}
