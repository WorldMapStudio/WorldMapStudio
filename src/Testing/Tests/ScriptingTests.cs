using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Covers the Phase 8 scripting core: that [ScriptProperty]/[ScriptFunction] reflection survives a
/// virtual override (the real case that broke a naive implementation — see
/// <see cref="EmptyEntity.DisplayName"/> overriding <see cref="Entity.DisplayName"/>), and that the
/// Jint engine's member filter actually restricts JS to the attributed surface rather than leaking
/// the whole CLR object.
/// </summary>
public static class ScriptingTests
{
    private sealed class BoxEntity : SceneEntity
    {
        public override SelfRotation SelfRotation => SelfRotation.None;

        public override Aabb LocalBounds => new(Vector3.Zero, Vector3.One);

        protected override Node3D BuildNode() => new();
    }

    private sealed class FixtureModule(SceneEntity[] entities) : IScriptModule
    {
        public string Name => "fixture";

        public float Priority => 0f;

        [ScriptFunction]
        public Entity[] All() => entities.Cast<Entity>().ToArray();

        [ScriptFunction]
        public int Add(int a, int b) => a + b;
    }

    private sealed class WidgetEntity : SceneEntity
    {
        [ScriptProperty(Mutable = true)]
        public string Label { get; set; } = "Widget";

        [ScriptFunction]
        public string Shout() => Label.ToUpperInvariant();

        public override SelfRotation SelfRotation => SelfRotation.None;

        public override Aabb LocalBounds => new(Vector3.Zero, Vector3.One);

        protected override Node3D BuildNode() => new();
    }

    private sealed class HandleFixtureModule(
        SceneEntityRegistry scene, CatalogEntityRegistry catalog, EditSessionManager sessions, SceneEntity entity)
        : IScriptModule
    {
        public string Name => "handles";

        public float Priority => 0f;

        [ScriptFunction]
        public ScriptEntityHandle Get() => new(scene, catalog, sessions, entity);
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void Reflection_finds_attributes_through_a_virtual_override()
    {
        var visible = ScriptReflection.Properties(typeof(EmptyEntity)).Select(p => p.Name).ToList();

        Assert.IsTrue(visible.Contains(nameof(Entity.DisplayName)),
            "EmptyEntity overrides DisplayName; [ScriptProperty] only lives on the Entity base declaration");
        Assert.IsTrue(visible.Contains(nameof(Entity.Id)));
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void Unattributed_members_are_not_reflected()
    {
        var visible = ScriptReflection.Properties(typeof(EmptyEntity)).Select(p => p.Name).ToList();

        Assert.IsFalse(visible.Contains(nameof(EmptyEntity.Shape)), "Shape has no [ScriptProperty] yet");
        Assert.IsFalse(visible.Contains(nameof(EmptyEntity.RecordId)));
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void Engine_calls_attributed_functions()
    {
        var host = new ScriptEngineHost([new FixtureModule([])]);

        Assert.AreEqual("3", host.Evaluate("wms.fixture.Add(1, 2).toString()"));
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void Engine_exposes_entities_returned_from_a_function()
    {
        var entity = new BoxEntity();
        var host = new ScriptEngineHost([new FixtureModule([entity])]);

        Assert.AreEqual(entity.DisplayName, host.Evaluate("wms.fixture.All()[0].DisplayName"));
        Assert.AreEqual(entity.Id.Value.ToString(), host.Evaluate("wms.fixture.All()[0].Id.Value.toString()"));
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void Engine_hides_members_without_a_script_attribute()
    {
        var host = new ScriptEngineHost([new FixtureModule([new BoxEntity()])]);

        // GetType is a real public CLR method every object has, but nothing marks it [ScriptFunction] —
        // if this ever comes back "function", the member filter has started leaking raw CLR surface.
        Assert.AreEqual("undefined", host.Evaluate("typeof wms.fixture.All()[0].GetType"));
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void Type_declarations_describe_the_bound_surface()
    {
        string dts = ScriptTypeDeclarationWriter.Generate([new FixtureModule([])]);

        Assert.IsTrue(dts.Contains("const fixture: FixtureModule;"));
        Assert.IsTrue(dts.Contains("All(): Entity[];"));
        Assert.IsTrue(dts.Contains("Add(a: number, b: number): number;"));
        Assert.IsTrue(dts.Contains("readonly DisplayName: string;"), "Entity's own properties must appear once discovered via a return type");
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void Set_applies_the_value_and_records_an_undoable_command()
    {
        var scene = new SceneEntityRegistry();
        var catalog = new CatalogEntityRegistry();
        var sessions = new EditSessionManager();
        var empty = new EmptyEntity { Name = "Torch" };
        scene.Add(empty);

        var handle = new ScriptEntityHandle(scene, catalog, sessions, empty);
        handle.Set(nameof(EmptyEntity.Name), "Lantern");

        Assert.AreEqual("Lantern", empty.Name);
        Assert.IsTrue(sessions.Active.IsDirty, "a scripted edit must pin its target like any other edit");

        sessions.Undo();
        Assert.AreEqual("Torch", empty.Name, "undo after a scripted edit must behave like undo after a gizmo drag");
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void Set_rejects_a_property_that_is_not_mutable()
    {
        var scene = new SceneEntityRegistry();
        var catalog = new CatalogEntityRegistry();
        var sessions = new EditSessionManager();
        var empty = new EmptyEntity();
        scene.Add(empty);

        var handle = new ScriptEntityHandle(scene, catalog, sessions, empty);
        Assert.Throws<System.InvalidOperationException>(() => handle.Set(nameof(Entity.DisplayName), "Nope"));
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void A_handle_fails_loudly_once_its_entity_is_gone()
    {
        var scene = new SceneEntityRegistry();
        var catalog = new CatalogEntityRegistry();
        var sessions = new EditSessionManager();
        var empty = new EmptyEntity();
        scene.Add(empty);

        var handle = new ScriptEntityHandle(scene, catalog, sessions, empty);
        scene.Remove(empty);

        Assert.Throws<System.InvalidOperationException>(() => handle.Get(nameof(Entity.DisplayName)),
            "a stale handle must throw, not silently read a zombie object");
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void Engine_reads_a_handle_property_by_direct_assignment_syntax()
    {
        var scene = new SceneEntityRegistry();
        var catalog = new CatalogEntityRegistry();
        var sessions = new EditSessionManager();
        var widget = new WidgetEntity { Label = "Torch" };
        scene.Add(widget);

        var host = new ScriptEngineHost([new HandleFixtureModule(scene, catalog, sessions, widget)]);

        Assert.AreEqual("Torch", host.Evaluate("wms.handles.Get().Label"));
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void Engine_writes_a_handle_property_by_direct_assignment_syntax()
    {
        var scene = new SceneEntityRegistry();
        var catalog = new CatalogEntityRegistry();
        var sessions = new EditSessionManager();
        var widget = new WidgetEntity { Label = "Torch" };
        scene.Add(widget);

        var host = new ScriptEngineHost([new HandleFixtureModule(scene, catalog, sessions, widget)]);
        host.Evaluate("wms.handles.Get().Label = 'Lantern'");

        Assert.AreEqual("Lantern", widget.Label, "the proxy's set trap must reach the real entity, not a copy");
        Assert.IsTrue(sessions.Active.IsDirty, "a direct-assignment write must still go through the edit session");
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void Engine_calls_an_entity_method_through_the_proxy()
    {
        var scene = new SceneEntityRegistry();
        var catalog = new CatalogEntityRegistry();
        var sessions = new EditSessionManager();
        var widget = new WidgetEntity { Label = "torch" };
        scene.Add(widget);

        var host = new ScriptEngineHost([new HandleFixtureModule(scene, catalog, sessions, widget)]);

        Assert.AreEqual("TORCH", host.Evaluate("wms.handles.Get().Shout()"));
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void Engine_rejects_assigning_a_property_that_is_not_mutable()
    {
        var scene = new SceneEntityRegistry();
        var catalog = new CatalogEntityRegistry();
        var sessions = new EditSessionManager();
        var widget = new WidgetEntity();
        scene.Add(widget);

        var host = new ScriptEngineHost([new HandleFixtureModule(scene, catalog, sessions, widget)]);

        Assert.IsTrue(host.Evaluate("wms.handles.Get().DisplayName = 'Nope'").StartsWith("Error:"),
            "DisplayName has no Mutable = true, so assignment from JS must fail, not silently no-op");
    }
}
