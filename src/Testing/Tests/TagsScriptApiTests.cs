using System.Linq;
using Godot;

namespace WorldMapStudio;

public static class TagsScriptApiTests
{
    private sealed class Fixture(EditorContext context, SceneEntity[] entities) : IScriptModule
    {
        public string Name => "fixture";

        [ScriptFunction]
        public ScriptEntityHandle[] All() => Take(0, entities.Length);

        // A subset as a CLR array: a JS array literal of handles can't be converted back to handles.
        [ScriptFunction]
        public ScriptEntityHandle[] Take(int start, int count) =>
            entities.Skip(start).Take(count)
                .Select(entity => new ScriptEntityHandle(context.Scene, context.Catalog, context.EditSessions, entity)).ToArray();
    }

    private static (EditorContext, ScriptEngineHost, MapSceneEntity[]) NewRig()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_tags_script_test__" });
        MapSceneEntity[] entities =
        [
            new() { Name = "A", Map = context.Maps.CurrentMap },
            new() { Name = "B", Map = context.Maps.CurrentMap },
            new() { Name = "C", Map = context.Maps.CurrentMap },
        ];
        foreach (MapSceneEntity entity in entities)
        {
            context.Scene.Add(entity);
        }

        var host = new ScriptEngineHost(
            [context.Scripting.TagsScriptApi, context.Scripting.SceneScriptApi, new Fixture(context, entities)]);
        return (context, host, entities);
    }

    [EditorTest(Category = "Tags Script", Thread = TestThread.Main)]
    public static void Create_add_list_and_find_by_name()
    {
        (EditorContext context, ScriptEngineHost host, MapSceneEntity[] entities) = NewRig();

        host.Evaluate("wms.tags.Create('town', 0xff8800)");
        Assert.AreEqual("2", host.Evaluate("wms.tags.Add(wms.fixture.Take(0, 2), 'town').toString()"));

        Assert.AreEqual("1", host.Evaluate("wms.tags.List().length.toString()"));
        Assert.AreEqual("2", host.Evaluate("wms.tags.List()[0].LoadedCount.toString()"));
        Assert.AreEqual("2", host.Evaluate("wms.tags.Find('town').length.toString()"));
        Assert.AreEqual("1", host.Evaluate("wms.tags.Get(wms.fixture.All()[0]).length.toString()"));
        Assert.IsTrue(entities[0].Tags.Contains(context.Tags.FindByName("town")!.RecordId!.Value));
        Assert.IsTrue(entities[2].Tags.IsEmpty);
    }

    [EditorTest(Category = "Tags Script", Thread = TestThread.Main)]
    public static void Add_and_remove_undo_and_a_unknown_name_is_refused()
    {
        (EditorContext context, ScriptEngineHost host, MapSceneEntity[] entities) = NewRig();
        host.Evaluate("wms.tags.Create('wip')");

        host.Evaluate("wms.tags.Add(wms.fixture.All(), 'wip')");
        Assert.IsTrue(entities.All(entity => !entity.Tags.IsEmpty));

        context.EditSessions.Undo();
        Assert.IsTrue(entities.All(entity => entity.Tags.IsEmpty));

        string error = host.Evaluate("try { wms.tags.Add(wms.fixture.All(), 'nope'); 'added' } catch (e) { e.message }");
        Assert.IsTrue(error.Contains("nope"), error);
    }

    [EditorTest(Category = "Tags Script", Thread = TestThread.Main)]
    public static void Set_replaces_tags_and_scene_all_filters_by_tag()
    {
        (_, ScriptEngineHost host, _) = NewRig();
        host.Evaluate("wms.tags.Create('a'); wms.tags.Create('b')");
        host.Evaluate("wms.tags.Add(wms.fixture.Take(0, 1), 'a')");

        host.Evaluate("wms.tags.Set(wms.fixture.Take(0, 2), ['b'])");

        Assert.AreEqual("0", host.Evaluate("wms.scene.All(null, 'a').length.toString()"));
        Assert.AreEqual("2", host.Evaluate("wms.scene.All(null, 'b').length.toString()"));
    }

    [EditorTest(Category = "Tags Script", Thread = TestThread.Main)]
    public static void Delete_takes_the_tag_off_entities()
    {
        (_, ScriptEngineHost host, MapSceneEntity[] entities) = NewRig();
        host.Evaluate("wms.tags.Create('gone'); wms.tags.Add(wms.fixture.All(), 'gone')");

        host.Evaluate("wms.tags.Delete('gone')");

        Assert.AreEqual("0", host.Evaluate("wms.tags.List().length.toString()"));
        Assert.IsTrue(entities.All(entity => entity.Tags.IsEmpty));
    }

    [EditorTest(Category = "Tags Script", Thread = TestThread.Main)]
    public static void The_object_filter_round_trips_and_refuses_a_contradiction()
    {
        (EditorContext context, ScriptEngineHost host, _) = NewRig();
        host.Evaluate("wms.tags.Create('x'); wms.tags.Create('y')");

        host.Evaluate("wms.tags.SetObjectFilter(['x'], ['y'], true)");
        Assert.IsTrue(context.Tags.ObjectFilter.IncludeUntagged);
        Assert.AreEqual("1", host.Evaluate("wms.tags.GetObjectFilter().Include.length.toString()"));
        Assert.AreEqual("1", host.Evaluate("wms.tags.GetObjectFilter().Exclude.length.toString()"));

        string error = host.Evaluate("try { wms.tags.SetObjectFilter(['x'], ['x']); 'set' } catch (e) { e.message }");
        Assert.IsTrue(error.Contains("both"), error);

        host.Evaluate("wms.tags.SetObjectFilter([], [])");
        Assert.IsFalse(context.Tags.ObjectFilter.IsActive);
    }

    [EditorTest(Category = "Tags Script", Thread = TestThread.Main)]
    public static void Scene_reports_native_entities_as_not_bridged()
    {
        (_, ScriptEngineHost host, _) = NewRig();

        Assert.AreEqual("false", host.Evaluate("wms.scene.IsBridged(wms.fixture.All()[0]).toString()"));
        Assert.AreEqual("true", host.Evaluate("(wms.scene.Source(wms.fixture.All()[0]) === null).toString()"));
    }
}
