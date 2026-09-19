using System;
using System.Linq;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>A discovered test, for a caller choosing what to run.</summary>
public sealed class TestDescriptor
{
    internal TestDescriptor(TestCase test)
    {
        Id = test.Id;
        Name = test.Name;
        Category = test.Category;
        Thread = test.Thread.ToString();
        Skip = test.SkipReason;
    }

    [ScriptProperty]
    public string Id { get; }

    [ScriptProperty]
    public string Name { get; }

    [ScriptProperty]
    public string Category { get; }

    /// <summary>Main or Background.</summary>
    [ScriptProperty]
    public string Thread { get; }

    /// <summary>Why the test is declared skipped, or null when it is not.</summary>
    [ScriptProperty]
    public string? Skip { get; }
}

/// <summary>A test run's state at one instant, counted over only the tests in that run.</summary>
public sealed class TestRunDescriptor
{
    internal TestRunDescriptor(TestRunStatus status)
    {
        TestRunSummary summary = status.Summary;

        Id = status.Id;
        State = status.State.ToString();
        IsRunning = status.IsRunning;
        RunningId = status.RunningId;
        Total = summary.Total;
        Passed = summary.Passed;
        Failed = summary.Failed;
        Errored = summary.Errored;
        Skipped = summary.Skipped;
        NotRun = summary.NotRun;
        DurationMs = summary.DurationMs;
        AllGreen = status.AllGreen;
    }

    [ScriptProperty]
    public long Id { get; }

    /// <summary>Queued, Executing, Completed, Faulted or Cancelled.</summary>
    [ScriptProperty]
    public string State { get; }

    [ScriptProperty]
    public bool IsRunning { get; }

    /// <summary>Id of the test executing right now, or empty.</summary>
    [ScriptProperty]
    public string RunningId { get; }

    [ScriptProperty]
    public int Total { get; }

    [ScriptProperty]
    public int Passed { get; }

    [ScriptProperty]
    public int Failed { get; }

    [ScriptProperty]
    public int Errored { get; }

    [ScriptProperty]
    public int Skipped { get; }

    /// <summary>Tests in the run that have not produced a result yet, or never will (a cancelled run).</summary>
    [ScriptProperty]
    public int NotRun { get; }

    /// <summary>Sum of the individual tests' own durations, not the run's wall clock.</summary>
    [ScriptProperty]
    public double DurationMs { get; }

    /// <summary>The run finished, nothing failed or errored, and every test in it ran.</summary>
    [ScriptProperty]
    public bool AllGreen { get; }
}

/// <summary>One test's latest result.</summary>
public sealed class TestResultDescriptor
{
    internal TestResultDescriptor(TestResultView result)
    {
        Id = result.Id;
        Name = result.Name;
        Category = result.Category;
        Outcome = result.Outcome.ToString();
        DurationMs = result.DurationMs;
        Message = result.Message;
        Details = result.Details;
        Logs = result.Logs;
    }

    [ScriptProperty]
    public string Id { get; }

    [ScriptProperty]
    public string Name { get; }

    [ScriptProperty]
    public string Category { get; }

    /// <summary>NotRun, Running, Passed, Failed, Errored or Skipped.</summary>
    [ScriptProperty]
    public string Outcome { get; }

    [ScriptProperty]
    public double DurationMs { get; }

    [ScriptProperty]
    public string Message { get; }

    /// <summary>The stack trace, for an errored test.</summary>
    [ScriptProperty]
    public string Details { get; }

    [ScriptProperty]
    public string[] Logs { get; }
}

/// <summary>
/// The in-editor test runner, exposed to JS as <c>wms.tests</c> — the same runner the Test Runner
/// window drives, so a run started here shows up live there and the reverse.
///
/// <see cref="Run"/> hands back a run id rather than a promise on purpose: a full run outlives the
/// script host's 60 second settlement limit, so awaiting one would report a spurious failure while the
/// tests carried on. Poll <see cref="Status"/>, or loop on <see cref="Wait"/>.
/// </summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class TestsScriptApi : IScriptModule
{
    /// <summary>Bounds one <see cref="Wait"/> call, not the run. Under the host's own settlement
    /// deadline, which is where this ceiling comes from.</summary>
    private const int MaxWaitMilliseconds = 45_000;

    private const int PollMilliseconds = 50;

    // Resolved per call: the runner discovers every test when first touched, which contexts built
    // by tests should not pay for.
    private readonly Func<TestRunner> _runner;

    public string Name => "tests";

    public TestsScriptApi(ScriptingSystem system)
        : this(() => system.Context.Tests)
    {
    }

    internal TestsScriptApi(TestRunner runner)
        : this(() => runner)
    {
    }

    private TestsScriptApi(Func<TestRunner> runner)
    {
        _runner = runner;
    }

    /// <summary>The discovered tests matching <paramref name="filter"/>: a case-insensitive substring
    /// of <c>Category.Name</c>, or a glob when it contains <c>*</c> or <c>?</c>. All of them when omitted.</summary>
    [ScriptFunction]
    public TestDescriptor[] List(string? filter = null) =>
        _runner().Select(filter).Select(test => new TestDescriptor(test)).ToArray();

    /// <summary>Starts a run over the tests matching <paramref name="filter"/> and returns its id.
    /// Throws when a run is already in progress or nothing matched. Never awaits — see the class remarks.</summary>
    [ScriptFunction]
    public long Run(string? filter = null)
    {
        TestRunner runner = _runner();
        return runner.TryRun(runner.Select(filter), out string? blocker)
            ?? throw new InvalidOperationException(blocker ?? "Could not start a test run.");
    }

    [ScriptFunction]
    public TestRunDescriptor Status(long runId) => new(Require(runId));

    /// <summary>
    /// Resolves with the run's status once it finishes, or once <paramref name="timeoutMs"/> elapses —
    /// whichever comes first. The timeout bounds this call only: it never cancels anything, and the
    /// run carries on either way.
    /// </summary>
    [ScriptFunction]
    public async Task<TestRunDescriptor> Wait(long runId, int timeoutMs = MaxWaitMilliseconds)
    {
        TestRunner runner = _runner();
        TestRunStatus status = Require(runId);
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Clamp(timeoutMs, 0, MaxWaitMilliseconds));

        while (status.IsRunning && DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollMilliseconds).ConfigureAwait(false);
            status = runner.Status(runId) ?? status;
        }

        return new TestRunDescriptor(status);
    }

    /// <summary>Stops the run after the test that is executing now. A run that is not in flight is unaffected.</summary>
    [ScriptFunction]
    public void Cancel(long runId)
    {
        Require(runId);
        _runner().Stop(runId);
    }

    /// <summary>The latest result of each test in the run, in run order. A test a later run has since
    /// re-run shows that later result.</summary>
    [ScriptFunction]
    public TestResultDescriptor[] Results(long runId, bool onlyFailures = false)
    {
        TestResultView[] results = _runner().Results(runId)
            ?? throw new InvalidOperationException($"No test run with id {runId}.");

        return results
            .Where(result => !onlyFailures || result.Outcome is TestOutcome.Failed or TestOutcome.Errored)
            .Select(result => new TestResultDescriptor(result))
            .ToArray();
    }

    /// <summary>Re-scans for tests and returns how many there are. Throws while a run is in progress.</summary>
    [ScriptFunction]
    public int Rediscover()
    {
        TestRunner runner = _runner();
        if (runner.IsRunning)
        {
            throw new InvalidOperationException("a test run is in progress");
        }

        runner.Rediscover();
        return runner.Cases.Count;
    }

    private TestRunStatus Require(long runId) =>
        _runner().Status(runId) ?? throw new InvalidOperationException($"No test run with id {runId}.");
}
