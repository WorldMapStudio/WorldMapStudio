using System.Linq;
using Godot;

namespace WorldMapStudio;

public static class PrefabsScriptApiTests
{
    private sealed class Fixture(EditorContext context, SceneEntity[] entities) : IScriptModule
    {
        public string Name => "fixture";

        [ScriptFunction]
        public ScriptEntityHandle[] All() =>
            entities.Select(entity => new ScriptEntityHandle(context.Scene, context.Catalog, context.EditSessions, entity)).ToArray();
    }

    [EditorTest(Category = "Prefabs Script", Thread = TestThread.Main)]
    public static void Save_spawn_undo_and_delete_a_two_entity_selection()
    {
        (EditorContext context, ScriptEngineHost host) = NewRig();

        Assert.AreEqual("2", host.Evaluate("wms.prefabs.Save(wms.fixture.All(), 'Pair').EntityCount.toString()"));

        int before = context.Scene.Entities.Count();
        Assert.AreEqual("2", host.Evaluate("var spawned = wms.prefabs.Spawn('Pair', 100, 0, 50); spawned.length.toString()"));
        Assert.AreEqual(before + 2, context.Scene.Entities.Count());

        // The anchor is the bottom-centre of both entities' combined bounds, so it lands on the spawn
        // point and the layout between them is kept.
        double firstX = double.Parse(host.Evaluate("wms.scene.GetPosition(spawned[0])[0].toString()"), System.Globalization.CultureInfo.InvariantCulture);
        double secondX = double.Parse(host.Evaluate("wms.scene.GetPosition(spawned[1])[0].toString()"), System.Globalization.CultureInfo.InvariantCulture);
        Assert.AreApproximatelyEqual(2.0, System.Math.Abs(secondX - firstX), 1e-4, "the layout is kept");
        Assert.AreApproximatelyEqual(100.0, (firstX + secondX) * 0.5, 1e-4, "the anchor lands on the spawn point");

        context.EditSessions.Undo();
        Assert.AreEqual(before, context.Scene.Entities.Count());

        host.Evaluate("wms.prefabs.Delete('Pair')");
        Assert.AreEqual("0", host.Evaluate("wms.prefabs.List().filter(p => p.Name == 'Pair').length.toString()"));
        Assert.AreEqual(0, context.Scene.Entities.Count(entity => entity.Map == PrefabSystem.LibraryMap));

        context.EditSessions.Undo();
        Assert.AreEqual("2", host.Evaluate("wms.prefabs.List().filter(p => p.Name == 'Pair')[0].EntityCount.toString()"));

        context.EditSessions.Undo();
        Assert.AreEqual("0", host.Evaluate("wms.prefabs.List().filter(p => p.Name == 'Pair').length.toString()"));
    }

    [EditorTest(Category = "Prefabs Script", Thread = TestThread.Main)]
    public static void Tags_survive_saving_and_spawning_a_prefab()
    {
        (EditorContext context, ScriptEngineHost host) = NewRig();
        EntityTagDefinition tag = context.Tags.Create("keep");
        SceneEntity tagged = context.Scene.Entities.First(entity => entity.Name == "First");
        context.Tags.Add([tagged], tag.RecordId!.Value);

        host.Evaluate("wms.prefabs.Save(wms.fixture.All(), 'Tagged')");
        host.Evaluate("wms.prefabs.Spawn('Tagged', 50, 0, 50)");

        SceneEntity[] spawned = context.Scene.Entities
            .Where(entity => entity.Map == context.Maps.CurrentMap && !ReferenceEquals(entity, tagged) && entity.Name == "First")
            .ToArray();
        Assert.AreEqual(1, spawned.Length);
        Assert.IsTrue(spawned[0].Tags.Contains(tag.RecordId!.Value), "the copy carries the tag");
        Assert.IsTrue(context.Scene.Entities.Any(entity => entity.Map == PrefabSystem.LibraryMap && entity.Tags.Contains(tag.RecordId!.Value)),
            "and so does the template");
    }

    [EditorTest(Category = "Prefabs Script", Thread = TestThread.Main)]
    public static void Spawn_can_select_the_new_entities()
    {
        (EditorContext context, ScriptEngineHost host) = NewRig();
        host.Evaluate("wms.prefabs.Save(wms.fixture.All(), 'Pair')");

        host.Evaluate("wms.prefabs.Spawn('Pair', 0, 0, 0, true)");

        Assert.AreEqual(2, context.Selection.Selected.Count);
    }

    [EditorTest(Category = "Prefabs Script", Thread = TestThread.Main)]
    public static void An_unknown_prefab_is_refused()
    {
        (_, ScriptEngineHost host) = NewRig();

        string error = host.Evaluate("try { wms.prefabs.Spawn('Nope', 0, 0, 0); 'spawned' } catch (e) { e.message }");

        Assert.IsTrue(error.Contains("Nope"), error);
    }

    private static (EditorContext, ScriptEngineHost) NewRig()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_prefabs_script_test__" });

        var first = new MapSceneEntity { Name = "First", Map = context.Maps.CurrentMap };
        var second = new MapSceneEntity { Name = "Second", Map = context.Maps.CurrentMap };
        second.Transform = new Transform3D(Basis.Identity, new Vector3(2, 0, 0));
        context.Scene.Add(first);
        context.Scene.Add(second);

        var host = new ScriptEngineHost([context.Scripting.PrefabsScriptApi, context.Scripting.SceneScriptApi, new Fixture(context, [first, second])]);
        return (context, host);
    }
}
