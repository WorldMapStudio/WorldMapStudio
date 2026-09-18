#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// Covers <c>wms.tests</c> and the runner behind it. Each test builds its own <see cref="TestRunner"/>
/// over hand-made cases: starting a run of the real registry from inside a test would queue behind the
/// run that is executing this very test.
/// </summary>
public static class TestsScriptApiTests
{
    private static TestCase Case(string category, string name, Func<TestContext, Task> body, string? skip = null) =>
        new($"{category}.{name}", name, category, TestThread.Background, skip, body);

    private static TestCase Passing(string category, string name) => Case(category, name, _ => Task.CompletedTask);

    private static TestCase Failing(string category, string name) =>
        Case(category, name, _ => throw new TestAssertException("deliberate failure"));

    private static TestsScriptApi ApiOver(params TestCase[] cases) => new(new TestRunner(null, cases));

    private static async Task<TestRunDescriptor> Finish(TestsScriptApi api, long runId)
    {
        TestRunDescriptor status = await api.Wait(runId, 30_000);
        Assert.IsFalse(status.IsRunning, "the run did not finish in time");
        return status;
    }

    [EditorTest(Category = "TestsScriptApi", Thread = TestThread.Background)]
    public static async Task Run_ids_increase()
    {
        TestsScriptApi api = ApiOver(Passing("Fixture", "One"));

        long first = api.Run();
        await Finish(api, first);
        long second = api.Run();
        await Finish(api, second);

        Assert.Greater(second, first);
    }

    [EditorTest(Category = "TestsScriptApi", Thread = TestThread.Background)]
    public static async Task Second_run_while_one_is_in_progress_throws()
    {
        TaskCompletionSource gate = new();
        TestsScriptApi api = ApiOver(Case("Fixture", "Gate", _ => gate.Task));

        long first = api.Run();
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => api.Run());
        gate.SetResult();
        TestRunDescriptor status = await Finish(api, first);

        Assert.IsTrue(error.Message.Contains("already in progress"), error.Message);
        Assert.AreEqual(1, status.Passed);
    }

    [EditorTest(Category = "TestsScriptApi", Thread = TestThread.Background)]
    public static void Filter_matching_nothing_throws()
    {
        TestsScriptApi api = ApiOver(Passing("Fixture", "One"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => api.Run("no-such-test"));

        Assert.IsTrue(error.Message.Contains("no tests matched"), error.Message);
    }

    [EditorTest(Category = "TestsScriptApi", Thread = TestThread.Background)]
    public static async Task Results_and_counts_are_scoped_to_their_run()
    {
        TestsScriptApi api = ApiOver(
            Passing("Alpha", "First"),
            Passing("Alpha", "Second"),
            Failing("Beta", "Broken"));

        long alphaRun = api.Run("Alpha.*");
        TestRunDescriptor alpha = await Finish(api, alphaRun);
        long betaRun = api.Run("Beta");
        TestRunDescriptor beta = await Finish(api, betaRun);

        Assert.AreEqual(2, alpha.Total);
        Assert.AreEqual(2, alpha.Passed);
        Assert.AreEqual(0, alpha.NotRun, "the Beta test is not part of the first run");
        Assert.IsTrue(alpha.AllGreen);

        Assert.AreEqual(1, beta.Total);
        Assert.AreEqual(1, beta.Failed);

        Assert.IsTrue(
            api.Results(alphaRun).All(result => result.Category == "Alpha"),
            "results must only include the run's own tests");
        Assert.AreEqual(2, api.Results(alphaRun).Length);
        Assert.AreEqual(1, api.Results(betaRun).Length);
    }

    [EditorTest(Category = "TestsScriptApi", Thread = TestThread.Background)]
    public static async Task Failing_run_is_not_all_green_and_reports_the_failure()
    {
        TestsScriptApi api = ApiOver(Passing("Fixture", "Fine"), Failing("Fixture", "Broken"));

        long runId = api.Run();
        TestRunDescriptor status = await Finish(api, runId);
        TestResultDescriptor[] failures = api.Results(runId, onlyFailures: true);

        Assert.IsFalse(status.AllGreen);
        Assert.AreEqual(1, status.Failed);
        Assert.AreEqual(1, failures.Length);
        Assert.AreEqual("Fixture.Broken", failures[0].Id);
        Assert.AreEqual("Failed", failures[0].Outcome);
        Assert.AreEqual("deliberate failure", failures[0].Message);
    }

    [EditorTest(Category = "TestsScriptApi", Thread = TestThread.Background)]
    public static async Task Skipped_tests_do_not_make_a_run_red()
    {
        TestsScriptApi api = ApiOver(Passing("Fixture", "Fine"), Case("Fixture", "Later", _ => Task.CompletedTask, skip: "not yet"));

        TestRunDescriptor status = await Finish(api, api.Run());

        Assert.AreEqual(1, status.Skipped);
        Assert.IsTrue(status.AllGreen);
    }

    [EditorTest(Category = "TestsScriptApi", Thread = TestThread.Background)]
    public static void Unknown_run_ids_throw()
    {
        TestsScriptApi api = ApiOver(Passing("Fixture", "One"));

        Assert.Throws<InvalidOperationException>(() => api.Status(999));
        Assert.Throws<InvalidOperationException>(() => api.Results(999));
        Assert.Throws<InvalidOperationException>(() => api.Cancel(999));
    }

    [EditorTest(Category = "TestsScriptApi", Thread = TestThread.Background)]
    public static void List_applies_the_filter()
    {
        TestsScriptApi api = ApiOver(Passing("Alpha", "First"), Passing("Alpha", "Second"), Passing("Beta", "Third"));

        Assert.AreEqual(3, api.List().Length);
        Assert.AreEqual(2, api.List("alpha").Length);
        Assert.AreEqual("Beta.Third", api.List("*.third").Single().Id);
    }

    [EditorTest(Category = "TestsScriptApi", Thread = TestThread.Background)]
    public static void Filter_matches_substring_or_glob_on_the_id()
    {
        Assert.IsTrue(TestFilter.Parse(null).Matches("Any.Thing"));
        Assert.IsTrue(TestFilter.Parse("  ").Matches("Any.Thing"));
        Assert.IsTrue(TestFilter.Parse("landscape").Matches("LandscapeGrid.Cells align"));
        Assert.IsTrue(TestFilter.Parse("grid.cells").Matches("LandscapeGrid.Cells align"));
        Assert.IsFalse(TestFilter.Parse("batch").Matches("LandscapeGrid.Cells align"));

        Assert.IsTrue(TestFilter.Parse("Landscape*.Cells*").Matches("LandscapeGrid.Cells align"));
        Assert.IsFalse(TestFilter.Parse("Grid*").Matches("LandscapeGrid.Cells align"), "a glob is anchored at both ends");
        Assert.IsTrue(TestFilter.Parse("*.Cells?align").Matches("LandscapeGrid.Cells align"));
        Assert.IsFalse(TestFilter.Parse("a.b*").Matches("aXb.c"), "regex metacharacters are literal");
    }
}
