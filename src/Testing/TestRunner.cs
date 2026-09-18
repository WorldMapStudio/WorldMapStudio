#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Owns the discovered <see cref="TestCase"/>s and their latest results, and runs them in-process on
/// the shared <see cref="WorkQueue"/> so a run shows up alongside other editor work and never blocks
/// rendering. Each test runs on its declared <see cref="TestThread"/>; the whole run is a single work
/// item that hops threads per test.
///
/// All mutable state is guarded by <see cref="_lock"/>; the UI reads via <see cref="Snapshot"/> and
/// <see cref="Summarize"/>, both of which return immutable copies.
/// </summary>
public sealed class TestRunner
{
    private sealed class MutableResult
    {
        public TestOutcome Outcome = TestOutcome.NotRun;
        public double DurationMs;
        public string Message = "";
        public string Details = "";
        public string[] Logs = Array.Empty<string>();
    }

    private sealed class RunRecord
    {
        public RunRecord(long id, List<TestCase> cases)
        {
            Id = id;
            Cases = cases;
        }

        public long Id { get; }
        public List<TestCase> Cases { get; }

        // Null only between the run being claimed and its work item being scheduled.
        public WorkHandle? Handle { get; set; }

        public bool IsActive => Handle is null || Handle.State is WorkState.Queued or WorkState.Executing;
    }

    private readonly object _lock = new();
    private readonly Node? _editorRoot;
    private readonly List<TestCase> _cases;
    private readonly Dictionary<string, MutableResult> _results;
    private readonly Dictionary<long, RunRecord> _runs = new();

    private RunRecord? _current;
    private long _nextRunId = 1;
    private string _runningId = "";

    public TestRunner(Node? editorRoot, IReadOnlyList<TestCase>? cases = null)
    {
        _editorRoot = editorRoot;
        _cases = (cases ?? TestRegistry.Discover()).ToList();
        _results = _cases.ToDictionary(c => c.Id, _ => new MutableResult());
    }

    /// <summary>All discovered tests, in stable display order.</summary>
    public IReadOnlyList<TestCase> Cases => _cases;

    /// <summary>True while a run is in flight.</summary>
    public bool IsRunning
    {
        get { lock (_lock) { return _current is { IsActive: true }; } }
    }

    /// <summary>Id of the most recent run, or 0 if none has started.</summary>
    public long CurrentRunId
    {
        get { lock (_lock) { return _current?.Id ?? 0; } }
    }

    /// <summary>Id of the test currently executing, or empty.</summary>
    public string RunningId
    {
        get { lock (_lock) { return _runningId; } }
    }

    /// <summary>Re-scans for tests, preserving prior results for tests that still exist.</summary>
    public void Rediscover()
    {
        if (IsRunning)
        {
            return;
        }

        IReadOnlyList<TestCase> found = TestRegistry.Discover();
        lock (_lock)
        {
            Dictionary<string, MutableResult> old = new(_results);
            _cases.Clear();
            _cases.AddRange(found);
            _results.Clear();
            foreach (TestCase c in found)
            {
                _results[c.Id] = old.TryGetValue(c.Id, out MutableResult? prev) ? prev : new MutableResult();
            }
        }
    }

    /// <summary>Runs every discovered test.</summary>
    public void RunAll() => Run(_cases);

    /// <summary>Re-runs only tests whose last outcome was a failure or error.</summary>
    public void RunFailed() =>
        Run(_cases.Where(c => SnapshotOutcome(c.Id) is TestOutcome.Failed or TestOutcome.Errored));

    /// <summary>Runs a specific set of tests (e.g. the currently filtered subset). Does nothing when
    /// it cannot start; use <see cref="TryRun"/> to learn why.</summary>
    public void Run(IEnumerable<TestCase> cases) => TryRun(cases, out _);

    /// <summary>The discovered tests whose id matches <paramref name="filter"/> — see <see cref="TestFilter"/>.
    /// A null or empty filter selects everything.</summary>
    public IReadOnlyList<TestCase> Select(string? filter)
    {
        TestFilter parsed = TestFilter.Parse(filter);
        lock (_lock)
        {
            return _cases.Where(c => parsed.Matches(c.Id)).ToList();
        }
    }

    /// <summary>
    /// Starts a run over <paramref name="cases"/> and returns its id, or null with the reason in
    /// <paramref name="blocker"/> when nothing started (a run already in progress, or no tests).
    /// </summary>
    public long? TryRun(IEnumerable<TestCase> cases, out string? blocker)
    {
        List<TestCase> toRun = cases.ToList();
        RunRecord run;

        lock (_lock)
        {
            if (_current is { IsActive: true })
            {
                blocker = "a test run is already in progress";
                return null;
            }

            if (toRun.Count == 0)
            {
                blocker = "no tests matched";
                return null;
            }

            foreach (TestCase c in toRun)
            {
                MutableResult r = _results[c.Id];
                r.Outcome = TestOutcome.NotRun;
                r.DurationMs = 0;
                r.Message = "";
                r.Details = "";
                r.Logs = Array.Empty<string>();
            }

            run = new RunRecord(_nextRunId++, toRun);
            _runs[run.Id] = run;
            _current = run;
        }

        // The whole suite is one work item that starts on the main thread; each test switches to its
        // own declared thread. Editor-touching tests (the default) therefore run where it's safe to.
        WorkHandle handle = WorkQueue.Schedule("Editor Tests", ctx => RunLoop(ctx, toRun), WorkThread.Main);
        lock (_lock)
        {
            run.Handle = handle;
        }

        blocker = null;
        return run.Id;
    }

    /// <summary>Requests the in-flight run to stop after the current test.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            _current?.Handle?.Cancel();
        }
    }

    /// <summary>Requests one specific run to stop, if it is the one in flight.</summary>
    public void Stop(long runId)
    {
        lock (_lock)
        {
            if (_current is { } current && current.Id == runId)
            {
                current.Handle?.Cancel();
            }
        }
    }

    /// <summary>The run's state and counts over only its own tests, or null for an unknown id.</summary>
    public TestRunStatus? Status(long runId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out RunRecord? run))
            {
                return null;
            }

            WorkState state = run.Handle?.State ?? WorkState.Queued;
            string runningId = _current == run ? _runningId : "";
            return new TestRunStatus(run.Id, state, runningId, SummarizeLocked(run.Cases));
        }
    }

    /// <summary>Latest result of each test in the run, in the run's order, or null for an unknown id.
    /// A test that a later run has since re-run shows that later result.</summary>
    public TestResultView[]? Results(long runId)
    {
        lock (_lock)
        {
            return _runs.TryGetValue(runId, out RunRecord? run) ? ViewsLocked(run.Cases) : null;
        }
    }

    private async Task RunLoop(WorkContext work, List<TestCase> toRun)
    {
        try
        {
            foreach (TestCase test in toRun)
            {
                if (work.IsCancellationRequested)
                {
                    break;
                }

                work.Step($"{test.Category}: {test.Name}");
                SetRunning(test.Id);
                await RunOne(work, test);
            }
        }
        finally
        {
            SetRunning("");
        }
    }

    private async Task RunOne(WorkContext work, TestCase test)
    {
        if (test.SkipReason is not null)
        {
            Record(test.Id, TestOutcome.Skipped, 0, test.SkipReason, "", Array.Empty<string>());
            return;
        }

        // Hop to the thread this test asked for before timing/running it.
        if (test.Thread == TestThread.Background)
        {
            await work.SwitchToBackground();
        }
        else
        {
            await work.SwitchToMain();
        }

        List<string> logs = new();
        TestContext ctx = new(work, _editorRoot, logs);
        long start = Stopwatch.GetTimestamp();
        try
        {
            await test.Invoke(ctx);
            Record(test.Id, TestOutcome.Passed, Elapsed(start), "", "", logs.ToArray());
        }
        catch (TestSkippedException e)
        {
            Record(test.Id, TestOutcome.Skipped, Elapsed(start), e.Message, "", logs.ToArray());
        }
        catch (TestAssertException e)
        {
            Record(test.Id, TestOutcome.Failed, Elapsed(start), e.Message, "", logs.ToArray());
        }
        catch (OperationCanceledException)
        {
            Record(test.Id, TestOutcome.Skipped, Elapsed(start), "Cancelled.", "", logs.ToArray());
        }
        catch (Exception e)
        {
            Record(test.Id, TestOutcome.Errored, Elapsed(start),
                $"{e.GetType().Name}: {e.Message}", e.StackTrace ?? "", logs.ToArray());
            GD.PushError($"[Tests] '{test.Id}' errored: {e}");
        }
    }

    /// <summary>Immutable snapshot of every test's latest result, in display order.</summary>
    public TestResultView[] Snapshot()
    {
        lock (_lock)
        {
            return ViewsLocked(_cases);
        }
    }

    /// <summary>Aggregate counts across all tests, including any that no run has touched.</summary>
    public TestRunSummary Summarize()
    {
        lock (_lock)
        {
            return SummarizeLocked(_cases);
        }
    }

    private TestResultView[] ViewsLocked(List<TestCase> cases)
    {
        List<TestResultView> views = new(cases.Count);
        foreach (TestCase c in cases)
        {
            if (!_results.TryGetValue(c.Id, out MutableResult? r))
            {
                continue; // dropped by a rediscover since the run was started
            }

            TestOutcome outcome = c.Id == _runningId ? TestOutcome.Running : r.Outcome;
            views.Add(new TestResultView(
                c.Id, c.Name, c.Category, c.Thread, outcome,
                r.DurationMs, r.Message, r.Details, r.Logs));
        }
        return views.ToArray();
    }

    private TestRunSummary SummarizeLocked(List<TestCase> cases)
    {
        int total = 0, passed = 0, failed = 0, errored = 0, skipped = 0, notRun = 0;
        double duration = 0;
        foreach (TestCase c in cases)
        {
            if (!_results.TryGetValue(c.Id, out MutableResult? r))
            {
                continue;
            }

            total++;
            duration += r.DurationMs;
            switch (r.Outcome)
            {
                case TestOutcome.Passed: passed++; break;
                case TestOutcome.Failed: failed++; break;
                case TestOutcome.Errored: errored++; break;
                case TestOutcome.Skipped: skipped++; break;
                default: notRun++; break;
            }
        }
        return new TestRunSummary(total, passed, failed, errored, skipped, notRun, duration);
    }

    private void SetRunning(string id)
    {
        lock (_lock)
        {
            _runningId = id;
        }
    }

    private TestOutcome SnapshotOutcome(string id)
    {
        lock (_lock)
        {
            return _results.TryGetValue(id, out MutableResult? r) ? r.Outcome : TestOutcome.NotRun;
        }
    }

    private void Record(string id, TestOutcome outcome, double durationMs, string message, string details, string[] logs)
    {
        lock (_lock)
        {
            if (!_results.TryGetValue(id, out MutableResult? r))
            {
                return;
            }
            r.Outcome = outcome;
            r.DurationMs = durationMs;
            r.Message = message ?? "";
            r.Details = details ?? "";
            r.Logs = logs ?? Array.Empty<string>();
        }
    }

    private static double Elapsed(long startTimestamp) =>
        (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
}
