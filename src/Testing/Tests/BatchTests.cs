using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>Covers the batch gate, the status channel and the state store's key rules.</summary>
public static class BatchTests
{
    private sealed class NoopOperation : IBatchOperation
    {
        public string Id => "wms.test.noop";

        public string DisplayName => "Noop";

        public string Description => "Does nothing.";

        public float Priority => 0.0f;

        public void DrawSettings()
        {
        }

        public JsonObject SaveSettings() => new();

        public void LoadSettings(JsonObject settings)
        {
        }

        public Task RunAsync(BatchContext context, WorkContext work) => Task.CompletedTask;
    }

    private sealed class PinCommand(IEntity target) : IEditCommand
    {
        public IReadOnlyList<IEntity> Targets { get; } = [target];

        public string Description => "Pin";

        public void Apply()
        {
        }

        public void Revert()
        {
        }
    }

    private sealed class PinnedEntity : IEntity
    {
        public EntityId Id { get; } = EntityId.Next();
    }

    [EditorTest(Category = "Batch", Thread = TestThread.Background)]
    public static void Status_snapshots_are_safe_while_a_worker_appends()
    {
        var holder = new BatchStatusHolder();
        using var stop = new CancellationTokenSource();

        Task writer = Task.Run(() =>
        {
            for (int i = 0; !stop.IsCancellationRequested && i < 20_000; i++)
            {
                holder.Log($"line {i}");
                holder.Step($"step {i}");
                holder.Progress(i / 20_000f);
            }
        });

        // Reading a snapshot must never see the live queue mid-append — that is what handing out a
        // copy under the lock buys, and it is the whole reason this test exists.
        for (int i = 0; i < 5_000; i++)
        {
            BatchStatus status = holder.Snapshot();
            foreach (string line in status.Log)
            {
                Assert.IsNotNull(line);
            }
        }

        stop.Cancel();
        writer.Wait(TimeSpan.FromSeconds(5));
    }

    [EditorTest(Category = "Batch", Thread = TestThread.Background)]
    public static void Status_log_stays_bounded()
    {
        var holder = new BatchStatusHolder();
        for (int i = 0; i < 5_000; i++)
        {
            holder.Log($"line {i}");
        }

        BatchStatus status = holder.Snapshot();

        Assert.IsTrue(status.Log.Count <= 500, $"log grew to {status.Log.Count}");
        Assert.AreEqual("line 4999", status.Log[status.Log.Count - 1], "newest line should be last");
    }

    [EditorTest(Category = "Batch", Thread = TestThread.Background)]
    public static void Reload_latch_keeps_the_first_reason()
    {
        var latch = new BatchReloadLatch();

        Assert.IsNull(latch.Reason);
        latch.Require("wrote 3 tiles");
        latch.Require("wrote 4 more");

        Assert.AreEqual("wrote 3 tiles", latch.Reason);
    }

    [EditorTest(Category = "Batch")]
    public static void State_refuses_reserved_keys()
    {
        var project = new Project { Name = "__wms_batch_state_test__" };
        var context = new EditorContext(new Node3D(), project);

        try
        {
            BatchOperationState state = context.Batch.State.For("wms.test.noop");

            // The settings blob lives under a $-prefixed key, so nothing writing ordinary state can
            // clobber it by naming it.
            Assert.Throws<ArgumentException>(() => state.SetAsync(BatchState.SettingsKey, "{}").Wait());
            Assert.Throws<ArgumentException>(() => state.RemoveAsync("$anything").Wait());
        }
        finally
        {
            ProjectStore.Delete(project);
        }
    }

    [EditorTest(Category = "Batch")]
    public static void A_dirty_session_blocks_a_run_and_a_session()
    {
        var project = new Project { Name = "__wms_batch_gate_test__" };
        var context = new EditorContext(new Node3D(), project);

        try
        {
            context.EditSessions.Active.Record(new PinCommand(new PinnedEntity()));
            Assert.IsTrue(context.EditSessions.Active.IsDirty);

            Assert.IsNull(context.Batch.TryStart(new NoopOperation(), overrides: null, out string? runBlocker));
            Assert.IsNotNull(runBlocker, "a dirty session should say why it refused");

            Assert.IsNull(context.Batch.TryOpenSession("test", out string? sessionBlocker));
            Assert.IsNotNull(sessionBlocker);

            // The refusal must not leave a half-opened session behind holding the gate.
            Assert.IsNull(context.Batch.ActiveSession);
        }
        finally
        {
            ProjectStore.Delete(project);
        }
    }
}
