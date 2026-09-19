using System.Linq;
using Godot;

namespace WorldMapStudio;

public static class PrefabsScriptApiTests
{
    private sealed class Fixture(EditorContext context, SceneEntity entity) : IScriptModule
    {
        public string Name => "fixture";

        [ScriptFunction]
        public ScriptEntityHandle Root() => new(context.Scene, context.Catalog, context.EditSessions, entity);
    }

    [EditorTest(Category = "Prefabs Script", Thread = TestThread.Main)]
    public static void Save_spawn_undo_and_delete_a_two_entity_hierarchy()
    {
        (EditorContext context, ScriptEngineHost host) = NewRig();

        Assert.AreEqual("2", host.Evaluate("wms.prefabs.Save(wms.fixture.Root(), 'Pair').EntityCount.toString()"));

        int before = context.Scene.Entities.Count();
        Assert.AreEqual("2", host.Evaluate("var spawned = wms.prefabs.Spawn('Pair', 100, 0, 50); spawned.length.toString()"));
        Assert.AreEqual(before + 2, context.Scene.Entities.Count());

        Assert.AreEqual("100,0,50", host.Evaluate("wms.scene.GetPosition(spawned[0]).join(',')"));
        Assert.AreEqual("102,0,50", host.Evaluate("wms.scene.GetPosition(spawned[1]).join(',')"));

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
    public static void Spawn_can_select_the_new_entities()
    {
        (EditorContext context, ScriptEngineHost host) = NewRig();
        host.Evaluate("wms.prefabs.Save(wms.fixture.Root(), 'Pair')");

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

        var root = new SceneEntity { Name = "Root", Map = context.Maps.CurrentMap };
        var child = new SceneEntity { Name = "Child", Map = context.Maps.CurrentMap, Parent = root };
        child.Transform = new Transform3D(Basis.Identity, new Vector3(2, 0, 0));
        context.Scene.Add(root);
        context.Scene.Add(child);

        var host = new ScriptEngineHost([context.Scripting.PrefabsScriptApi, context.Scripting.SceneScriptApi, new Fixture(context, root)]);
        return (context, host);
    }
}
