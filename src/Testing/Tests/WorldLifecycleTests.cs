using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Covers the algorithmic core of <see cref="WorldLifecycle"/> (ordering and unload resilience,
/// exercised against fake participants rather than a live <see cref="EditorContext"/>) and the pieces
/// of the abort/reload wiring that don't need one either: <see cref="EditSessionManager"/>'s reload
/// request and its exclusive-operation guard on <see cref="EditSessionManager.Record"/>, and the
/// registry <c>Clear</c> methods a world unload relies on.
/// </summary>
public static class WorldLifecycleTests
{
    private sealed class FakeEntity : IEntity
    {
        public EntityId Id { get; } = EntityId.Next();
    }

    private sealed class FakeCommand(FakeEntity entity) : IEditCommand
    {
        public IReadOnlyList<IEntity> Targets { get; } = new IEntity[] { entity };
        public string Description => "Fake edit";
        public void Apply() { }
        public void Revert() { }
    }

    private sealed class FakeParticipant : IWorldParticipant
    {
        private readonly List<string> _log;

        public FakeParticipant(List<string> log, string name)
        {
            _log = log;
            Name = name;
        }

        public string Name { get; }
        public float LoadPriority { get; init; }
        public string? LoadStep { get; init; }
        public bool ThrowOnUnload { get; init; }

        void IWorldParticipant.LoadWorld() => _log.Add($"load:{Name}");

        void IWorldParticipant.UnloadWorld()
        {
            if (ThrowOnUnload)
            {
                throw new InvalidOperationException($"{Name} broke");
            }

            _log.Add($"unload:{Name}");
        }
    }

    [EditorTest(Category = "WorldLifecycle", Thread = TestThread.Background)]
    public static void Load_runs_ascending_priority_and_unload_runs_exact_reverse()
    {
        var log = new List<string>();
        var a = new FakeParticipant(log, "A") { LoadPriority = 0f };
        var b = new FakeParticipant(log, "B") { LoadPriority = 1f };
        var c = new FakeParticipant(log, "C") { LoadPriority = 2f };

        // Deliberately out of order, the way subsystem discovery would hand them over.
        IWorldParticipant[] participants = [c, a, b];

        WorldLifecycle.RunLoad(participants, null);
        Assert.AreEqual("load:A,load:B,load:C", string.Join(",", log));

        log.Clear();
        WorldLifecycle.RunUnload(participants);
        Assert.AreEqual("unload:C,unload:B,unload:A", string.Join(",", log));
    }

    [EditorTest(Category = "WorldLifecycle", Thread = TestThread.Background)]
    public static void A_participant_that_throws_on_unload_does_not_stop_the_rest()
    {
        var log = new List<string>();
        var first = new FakeParticipant(log, "First") { LoadPriority = 0f };
        var broken = new FakeParticipant(log, "Broken") { LoadPriority = 1f, ThrowOnUnload = true };
        var second = new FakeParticipant(log, "Second") { LoadPriority = 2f };

        List<string> errors = WorldLifecycle.RunUnload([first, broken, second]);

        Assert.AreEqual(2, log.Count, "both healthy participants still unloaded despite the broken one");
        Assert.IsTrue(log.Contains("unload:First"));
        Assert.IsTrue(log.Contains("unload:Second"));
        Assert.AreEqual(1, errors.Count, "the failure is reported rather than swallowed");
        Assert.IsTrue(errors[0].Contains("Broken"), "the report names which participant failed");
    }

    [EditorTest(Category = "WorldLifecycle", Thread = TestThread.Background)]
    public static void Load_reports_only_the_steps_participants_declare()
    {
        var log = new List<string>();
        var steps = new List<string>();
        var narrated = new FakeParticipant(log, "Maps") { LoadStep = "Loading maps" };
        var silent = new FakeParticipant(log, "Selection") { LoadPriority = 1f };

        WorldLifecycle.RunLoad([narrated, silent], steps.Add);

        Assert.AreEqual(1, steps.Count);
        Assert.AreEqual("Loading maps", steps[0]);
    }

    [EditorTest(Category = "EditSession", Thread = TestThread.Background)]
    public static void Abort_requests_a_reload_once_it_has_reverted_in_memory()
    {
        var entity = new FakeEntity();
        var order = new List<string>();
        var sessions = new EditSessionManager(EditSessionBindings.None with { RequestReload = () => order.Add("reload-requested") });

        sessions.Record(new FakeCommand(entity));
        Assert.IsTrue(sessions.Active.IsDirty);

        sessions.Abort();

        Assert.IsFalse(sessions.Active.IsDirty, "abort still reverts in memory");
        Assert.AreEqual(1, order.Count, "and, unlike AbortInMemory, also asks for a reload");
    }

    [EditorTest(Category = "EditSession", Thread = TestThread.Background)]
    public static void AbortInMemory_never_requests_a_reload()
    {
        var entity = new FakeEntity();
        var reloadRequested = false;
        var sessions = new EditSessionManager(EditSessionBindings.None with { RequestReload = () => reloadRequested = true });

        sessions.Record(new FakeCommand(entity));
        sessions.AbortInMemory();

        Assert.IsFalse(sessions.Active.IsDirty);
        Assert.IsFalse(reloadRequested, "the reload itself calls this and must not recurse into another one");
    }

    [EditorTest(Category = "EditSession", Thread = TestThread.Background)]
    public static void Recording_is_refused_while_an_exclusive_operation_is_active()
    {
        string? active = "Batch Import";
        var sessions = new EditSessionManager(EditSessionBindings.None with { ActiveOperation = () => active });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => sessions.Record(new FakeCommand(new FakeEntity())));
        Assert.IsTrue(error.Message.Contains("Batch Import"), "names which operation is blocking the edit");

        active = null;
        sessions.Record(new FakeCommand(new FakeEntity()));
        Assert.IsTrue(sessions.Active.IsDirty, "recording succeeds again once the operation clears");
    }

    [EditorTest(Category = "Scene", Thread = TestThread.Background)]
    public static void Clearing_the_scene_registry_drops_peripheral_and_resident_flags_too()
    {
        var registry = new SceneEntityRegistry();
        var entity = new SceneEntity();
        registry.Add(entity);
        registry.SetPeripheral(entity, true);
        registry.SetResident(entity, true);
        int beforeVersion = registry.Version;

        registry.Clear();

        Assert.AreEqual(0, registry.Entities.Count);
        Assert.IsFalse(registry.IsPeripheral(entity));
        Assert.IsFalse(registry.IsResident(entity));
        Assert.Greater(registry.Version, beforeVersion);

        int afterVersion = registry.Version;
        registry.Clear();
        Assert.AreEqual(afterVersion, registry.Version, "clearing an already-empty registry is a no-op");
    }

    private sealed class FakeCatalogEntity : CatalogEntity
    {
        public override string DisplayName => "Fake";
    }

    [EditorTest(Category = "Catalog", Thread = TestThread.Background)]
    public static void Clearing_the_catalog_registry_drops_every_type()
    {
        var registry = new CatalogEntityRegistry();
        registry.Add(new FakeCatalogEntity());
        registry.Add(new FakeCatalogEntity());

        registry.Clear();

        Assert.AreEqual(0, registry.Entities.Count);
    }
}
