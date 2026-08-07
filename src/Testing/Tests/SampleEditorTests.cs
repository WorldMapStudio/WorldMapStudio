using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Example tests that double as a smoke test for the runner itself. Delete or replace these with real
/// tests; they show the supported shapes: sync, async, thread control, editor access, and skips.
/// </summary>
public static class SampleEditorTests
{
    [EditorTest(Category = "Sanity")]
    public static void Arithmetic_holds()
    {
        Assert.AreEqual(4, 2 + 2);
        Assert.Greater(10, 3);
    }

    [EditorTest(Category = "Sanity")]
    public static void Throws_as_expected()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => throw new ArgumentException("bad arg"));
        Assert.AreEqual("bad arg", ex.Message);
    }

    [EditorTest(Category = "Sanity", Name = "This one fails on purpose")]
    public static void Intentional_failure()
    {
        Assert.AreEqual(expected: 42, actual: 7, "demonstrating a failing assertion");
    }

    [EditorTest(Category = "Editor")]
    public static void Editor_root_is_available(TestContext t)
    {
        Assert.IsNotNull(t.EditorRoot, "TestContext.EditorRoot should be set to the scene root.");
        t.Log($"Editor root is a {t.EditorRoot!.GetType().Name} named '{t.EditorRoot.Name}'.");
        t.Log($"It has {t.EditorRoot.GetChildCount()} child node(s).");
    }

    [EditorTest(Category = "Editor")]
    public static void Work_queue_has_workers(TestContext t)
    {
        WorkQueue.Initialize();
        t.Log($"Worker count: {WorkQueue.WorkerCount}");
        Assert.Greater(WorkQueue.WorkerCount, 0);
    }

    [EditorTest(Category = "Async", Thread = TestThread.Background)]
    public static async Task Background_work_completes(TestContext t)
    {
        t.Log("Starting on a background worker.");
        Thread.Sleep(20);
        await t.SwitchToMain();
        t.Log("Now back on the main thread; editor is safe to touch here.");
        Assert.IsNotNull(t.EditorRoot);
    }

    [EditorTest(Category = "Async", Skip = "Illustrates a skipped test")]
    public static void Not_ready_yet()
    {
        Assert.Fail("should never run while skipped");
    }
}
