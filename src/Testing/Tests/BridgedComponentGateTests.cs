using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>Covers which component kinds may be attached to an entity stored outside the editor's tables.</summary>
public static class BridgedComponentGateTests
{
    private sealed class ExternalEntity : SceneEntity
    {
    }

    private sealed class Fixture(EditorContext context, SceneEntity entity) : IScriptModule
    {
        public string Name => "fixture";

        [ScriptFunction]
        public ScriptEntityHandle Entity() => new(context.Scene, context.Catalog, context.EditSessions, entity);
    }

    [EditorTest(Category = "Bridge", Thread = TestThread.Main)]
    public static void Map_only_kinds_are_refused_on_a_bridged_entity_and_allowed_on_a_native_one()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_bridged_gate_test__" });
        var bridged = new ExternalEntity();
        var native = new MapSceneEntity();

        ISceneComponentType stamp = context.ComponentTypes.Find(StampComponent.Kind)!;
        ISceneComponentType marker = context.ComponentTypes.Find(MarkerComponent.Kind)!;

        Assert.IsFalse(stamp.CanAddTo(bridged), "a stamp is a landscape input that only ever resolves through map entities");
        Assert.IsTrue(stamp.CanAddTo(native));
        Assert.IsTrue(marker.CanAddTo(bridged), "a marker has no map-scoped persistence");
        Assert.IsTrue(marker.CanAddTo(native));
    }

    [EditorTest(Category = "Bridge", Thread = TestThread.Main)]
    public static void The_scripting_path_goes_through_the_same_check()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_bridged_gate_test__" });
        var bridged = new ExternalEntity();
        context.Scene.Add(bridged);
        var host = new ScriptEngineHost([context.Scripting.SceneScriptApi, new Fixture(context, bridged)]);

        string refused = host.Evaluate($"try {{ wms.scene.AddComponent(wms.fixture.Entity(), '{StampComponent.Kind}'); 'added' }} catch (e) {{ e.message }}");
        Assert.IsTrue(refused.Contains("can't be added"), refused);
        Assert.IsNull(bridged.Component<StampComponent>());

        host.Evaluate($"wms.scene.AddComponent(wms.fixture.Entity(), '{MarkerComponent.Kind}')");
        Assert.IsNotNull(bridged.Attached<MarkerComponent>());
    }

    [EditorTest(Category = "Bridge", Thread = TestThread.Main)]
    public static void An_intrinsic_component_cannot_be_removed_by_script()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_bridged_gate_test__" });
        var bridged = new ExternalEntity();
        bridged.AddIntrinsicComponent(new MarkerComponent());
        context.Scene.Add(bridged);
        var host = new ScriptEngineHost([context.Scripting.SceneScriptApi, new Fixture(context, bridged)]);

        string refused = host.Evaluate($"try {{ wms.scene.RemoveComponent(wms.fixture.Entity(), '{MarkerComponent.Kind}'); 'removed' }} catch (e) {{ e.message }}");

        Assert.IsTrue(refused.Contains("can't be removed"), refused);
        Assert.IsTrue(bridged.Components.Any(component => component.IsIntrinsic));
    }
}
