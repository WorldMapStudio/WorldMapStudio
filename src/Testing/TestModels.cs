#nullable enable
using System;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>Result of a single test run.</summary>
public enum TestOutcome
{
    NotRun,   // discovered but never executed in this session
    Running,  // currently executing
    Passed,   // finished without throwing
    Failed,   // an assertion failed (TestAssertException)
    Errored,  // threw an unexpected exception
    Skipped,  // marked [EditorTest(Skip=...)] or called Assert.Skip
}

/// <summary>
/// A discovered, ready-to-run test. <see cref="Invoke"/> is a pre-built adapter that normalises the
/// various allowed method signatures into a single awaitable call.
/// </summary>
public sealed class TestCase
{
    public TestCase(string id, string name, string category, TestThread thread, string? skipReason, Func<TestContext, Task> invoke)
    {
        Id = id;
        Name = name;
        Category = category;
        Thread = thread;
        SkipReason = skipReason;
        Invoke = invoke;
    }

    /// <summary>Stable unique id (<c>Category.Name</c>), used for UI state and lookups.</summary>
    public string Id { get; }

    public string Name { get; }
    public string Category { get; }
    public TestThread Thread { get; }

    /// <summary>Non-null when the test is declared as skipped via the attribute.</summary>
    public string? SkipReason { get; }

    internal Func<TestContext, Task> Invoke { get; }
}

/// <summary>Immutable view of a test's latest result, safe to read on the UI thread.</summary>
public readonly record struct TestResultView(
    string Id,
    string Name,
    string Category,
    TestThread Thread,
    TestOutcome Outcome,
    double DurationMs,
    string Message,
    string Details,
    string[] Logs)
{
    public bool HasDetails => Message.Length > 0 || Details.Length > 0 || Logs.Length > 0;
}

/// <summary>Aggregate counts for a completed or in-progress run.</summary>
public readonly record struct TestRunSummary(
    int Total,
    int Passed,
    int Failed,
    int Errored,
    int Skipped,
    int NotRun,
    double DurationMs)
{
    public int Ran => Passed + Failed + Errored + Skipped;
    public bool AllGreen => Failed == 0 && Errored == 0;
}
