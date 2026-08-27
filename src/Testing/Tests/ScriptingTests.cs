using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HttpClient = System.Net.Http.HttpClient;
using HttpResponseMessage = System.Net.Http.HttpResponseMessage;
using StringContent = System.Net.Http.StringContent;

namespace WorldMapStudio;

/// <summary>
/// Covers the Phase 8 scripting core: that [ScriptProperty]/[ScriptFunction] reflection survives a
/// virtual override (the real case that broke a naive implementation), and that the
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

        // Accepts a handle as an argument, so a test can prove a handle returned from one
        // [ScriptFunction] round-trips correctly when passed into another one.
        [ScriptFunction]
        public string Describe(ScriptEntityHandle handle) => $"describe:{handle.Resolve().DisplayName}";
    }

    private sealed class AsyncFixtureModule : IScriptModule
    {
        public string Name => "asyncFixture";

        public float Priority => 0f;

        [ScriptFunction]
        public async Task<string> LoadAsync()
        {
            await Task.Delay(50).ConfigureAwait(false);
            return "loaded";
        }

        [ScriptFunction]
        public async Task FailAsync()
        {
            await Task.Delay(20).ConfigureAwait(false);
            throw new InvalidOperationException("boom");
        }
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void Reflection_finds_attributes_through_a_virtual_override()
    {
        var visible = ScriptReflection.Properties(typeof(SceneEntity)).Select(p => p.Name).ToList();

        Assert.IsTrue(visible.Contains(nameof(Entity.DisplayName)),
            "SceneEntity overrides DisplayName; [ScriptProperty] only lives on the Entity base declaration");
        Assert.IsTrue(visible.Contains(nameof(Entity.Id)));
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void Unattributed_members_are_not_reflected()
    {
        var visible = ScriptReflection.Properties(typeof(SceneEntity)).Select(p => p.Name).ToList();

        Assert.IsFalse(visible.Contains(nameof(SceneEntity.RecordId)));
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
        var empty = new SceneEntity { Name = "Torch" };
        scene.Add(empty);

        var handle = new ScriptEntityHandle(scene, catalog, sessions, empty);
        handle.Set(nameof(SceneEntity.Name), "Lantern");

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
        var empty = new SceneEntity();
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
        var empty = new SceneEntity();
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

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static async Task Engine_resolves_an_awaited_task_without_blocking()
    {
        var host = new ScriptEngineHost([new AsyncFixtureModule()]);
        host.Evaluate("""
            var outcome = 'pending';
            (async function () {
                outcome = await wms.asyncFixture.LoadAsync();
            })();
            """);

        // host.Update() is the only thing driving progress here — if this loop ever needed to block
        // on the underlying Task instead of polling Update(), that would be exactly the main-thread
        // deadlock this bridge exists to avoid (see [[godot-main-thread-async-deadlock]]).
        for (int i = 0; i < 40 && host.Evaluate("outcome") == "pending"; i++)
        {
            host.Update();
            await Task.Delay(25);
        }

        Assert.AreEqual("loaded", host.Evaluate("outcome"));
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static async Task Engine_rejects_the_promise_when_the_task_faults()
    {
        var host = new ScriptEngineHost([new AsyncFixtureModule()]);
        host.Evaluate("""
            var outcome = 'pending';
            (async function () {
                try {
                    await wms.asyncFixture.FailAsync();
                    outcome = 'should not resolve';
                } catch (e) {
                    outcome = 'caught';
                }
            })();
            """);

        for (int i = 0; i < 40 && host.Evaluate("outcome") == "pending"; i++)
        {
            host.Update();
            await Task.Delay(25);
        }

        Assert.AreEqual("caught", host.Evaluate("outcome"), "a faulted Task must reject the JS Promise, not hang forever");
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static async Task Time_wait_resolves_after_the_engine_is_pumped()
    {
        var host = new ScriptEngineHost([new TimeFixtureModule()]);
        host.Evaluate("""
            var outcome = 'pending';
            (async function () {
                await wms.time.Wait(30);
                outcome = 'done';
            })();
            """);

        for (int i = 0; i < 40 && host.Evaluate("outcome") == "pending"; i++)
        {
            host.Update();
            await Task.Delay(15);
        }

        Assert.AreEqual("done", host.Evaluate("outcome"));
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static async Task EvaluateAsync_reports_the_value_an_async_script_settled_on()
    {
        // The synchronous Evaluate cannot wait, so it can only ever hand back "[object Promise]".
        // EvaluateAsync is what the HTTP endpoint (and so MCP) calls, and it has to report the value —
        // otherwise every asynchronous part of this API is unreachable from the transport it was
        // built for.
        var host = new ScriptEngineHost([new AsyncFixtureModule()]);
        Task<ScriptResult> pending = host.EvaluateAsync(
            "(async function () { return await wms.asyncFixture.LoadAsync(); })()");

        for (int i = 0; i < 80 && !pending.IsCompleted; i++)
        {
            host.Update();
            await Task.Delay(25);
        }

        Assert.IsTrue(pending.IsCompleted, "the request must not hang once its promise settles");

        ScriptResult result = await pending;
        Assert.IsTrue(result.Success, result.Output);
        Assert.AreEqual("loaded", result.Output);
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static async Task EvaluateAsync_fails_the_request_when_the_script_rejects()
    {
        var host = new ScriptEngineHost([new AsyncFixtureModule()]);
        Task<ScriptResult> pending = host.EvaluateAsync(
            "(async function () { return await wms.asyncFixture.FailAsync(); })()");

        for (int i = 0; i < 80 && !pending.IsCompleted; i++)
        {
            host.Update();
            await Task.Delay(25);
        }

        Assert.IsTrue(pending.IsCompleted, "a rejection has to end the request, not hang it");

        ScriptResult result = await pending;
        Assert.IsFalse(result.Success, "a rejected promise is a failed request, not a successful one");
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static async Task EvaluateAsync_still_answers_a_plain_synchronous_script()
    {
        var host = new ScriptEngineHost([new AsyncFixtureModule()]);
        Task<ScriptResult> pending = host.EvaluateAsync("1 + 1");

        for (int i = 0; i < 20 && !pending.IsCompleted; i++)
        {
            host.Update();
            await Task.Delay(10);
        }

        ScriptResult result = await pending;
        Assert.IsTrue(result.Success, result.Output);
        Assert.AreEqual("2", result.Output);
    }

    // TimeScriptApi's real constructor takes ScriptingSystem, which needs a live EditorContext to
    // build — this local double has the identical [ScriptFunction] surface without that dependency,
    // matching how ScriptEntityHandle's own tests avoid constructing EditorContext for the same reason.
    private sealed class TimeFixtureModule : IScriptModule
    {
        public string Name => "time";

        public float Priority => 0f;

        [ScriptFunction]
        public Task Wait(int milliseconds) => Task.Delay(milliseconds);
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void MapDescriptor_exposes_id_and_name()
    {
        var map = new Map(new MapId(3), "Eastern Kingdoms");
        var descriptor = new MapDescriptor(map);

        Assert.AreEqual(3, descriptor.Id);
        Assert.AreEqual("Eastern Kingdoms", descriptor.Name);
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void Events_fires_selection_changed_exactly_once_per_change()
    {
        var selection = new SelectionSystem();
        var events = new EventsScriptApi(selection, () => 0);
        int fired = 0;
        events.On("selectionChanged", () => fired++);

        events.Update();
        Assert.AreEqual(0, fired, "no change yet");

        selection.Add(new SceneEntity());
        events.Update();
        Assert.AreEqual(1, fired);

        events.Update();
        Assert.AreEqual(1, fired, "must not re-fire without a further change");
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void Events_off_removes_every_handler_for_that_name()
    {
        var selection = new SelectionSystem();
        var events = new EventsScriptApi(selection, () => 0);
        int fired = 0;
        events.On("selectionChanged", () => fired++);
        events.Off("selectionChanged");

        selection.Add(new SceneEntity());
        events.Update();

        Assert.AreEqual(0, fired);
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void Engine_registers_a_js_callback_for_an_event()
    {
        var selection = new SelectionSystem();
        var events = new EventsScriptApi(selection, () => 0);
        var host = new ScriptEngineHost([events]);
        host.Evaluate("var fired = 0; wms.events.On('selectionChanged', function () { fired++; });");

        selection.Add(new SceneEntity());
        events.Update();

        Assert.AreEqual("1", host.Evaluate("fired.toString()"));
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static void Engine_can_pass_a_handle_it_received_back_into_another_function()
    {
        // The risky case Phase 11 needed to confirm before ViewportScriptApi.Focus/SceneScriptApi.Delete
        // (both take a ScriptEntityHandle parameter) could be trusted: a handle returned from one
        // [ScriptFunction] and passed as an argument into another must round-trip to the same CLR
        // handle instance through Jint's Proxy machinery, not some copy or an unresolvable JS object.
        var scene = new SceneEntityRegistry();
        var catalog = new CatalogEntityRegistry();
        var sessions = new EditSessionManager();
        var widget = new WidgetEntity { Label = "Torch" };
        scene.Add(widget);

        var host = new ScriptEngineHost([new HandleFixtureModule(scene, catalog, sessions, widget)]);

        Assert.AreEqual($"describe:{widget.DisplayName}", host.Evaluate("wms.handles.Describe(wms.handles.Get())"));
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static async Task Http_run_endpoint_evaluates_and_returns_json()
    {
        var host = new ScriptEngineHost([new FixtureModule([])]);
        var server = new ScriptHttpServer(host, port: 18765);
        server.Start();

        await WithMainThreadPump(host, async () =>
        {
            using var client = new HttpClient();
            using HttpResponseMessage response = await client.PostAsync(
                "http://127.0.0.1:18765/run", new StringContent("wms.fixture.Add(2, 3).toString()"));
            string body = await response.Content.ReadAsStringAsync();

            Assert.IsTrue(body.Contains("\"ok\":true"), body);
            Assert.IsTrue(body.Contains("\"result\":\"5\""), body);
        });
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static async Task Http_run_endpoint_reports_a_script_error_as_json_not_a_500()
    {
        var host = new ScriptEngineHost([new FixtureModule([])]);
        var server = new ScriptHttpServer(host, port: 18766);
        server.Start();

        await WithMainThreadPump(host, async () =>
        {
            using var client = new HttpClient();
            using HttpResponseMessage response = await client.PostAsync(
                "http://127.0.0.1:18766/run", new StringContent("wms.fixture.NoSuchMethod()"));
            string body = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode,
                "a script error is a normal, well-formed response, not a transport failure");
            Assert.IsTrue(body.Contains("\"ok\":false"), body);
        });
    }

    [EditorTest(Category = "Scripting", Thread = TestThread.Background)]
    public static async Task Http_health_endpoint_responds_without_touching_the_engine()
    {
        var host = new ScriptEngineHost([]);
        var server = new ScriptHttpServer(host, port: 18767);
        server.Start();

        using var client = new HttpClient();
        using HttpResponseMessage response = await client.GetAsync("http://127.0.0.1:18767/health");
        string body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(body.Contains("\"ok\":true"), body);
    }

    // Runs `action` while a background loop pumps ScriptEngineHost.Update() every 10ms — needed
    // because /run's response only completes once Update() dequeues and evaluates it, the same
    // reason the async-bridge tests above poll Update() themselves rather than blocking on the Task.
    private static async Task WithMainThreadPump(ScriptEngineHost host, Func<Task> action)
    {
        using var cts = new CancellationTokenSource();
        Task pump = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                host.Update();
                await Task.Delay(10);
            }
        });

        try
        {
            await action().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            cts.Cancel();
            await pump;
        }
    }
}
