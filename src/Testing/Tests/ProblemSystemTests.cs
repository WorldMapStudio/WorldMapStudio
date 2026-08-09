using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Covers how problems come and go. The failure that matters here is a list that only ever grows:
/// once fixed problems linger, the window stops being worth opening.
/// </summary>
public static class ProblemSystemTests
{
    private static Problem Make(string key, ProblemSeverity severity = ProblemSeverity.Error, string kind = "Test") =>
        new()
        {
            Key = key,
            Severity = severity,
            Category = "Landscape",
            Kind = kind,
            Message = $"problem {key}",
        };

    [EditorTest(Category = "Problems", Thread = TestThread.Background)]
    public static void Replacing_a_scope_retracts_what_it_said_before()
    {
        // A chunk that rebuilds cleanly reports an empty list, and its old problems must go with it.
        var problems = new ProblemSystem();
        problems.Replace("chunk:0,0", [Make("a"), Make("b")]);
        Assert.AreEqual(2, problems.Count);

        problems.Replace("chunk:0,0", []);

        Assert.AreEqual(0, problems.Count);
    }

    [EditorTest(Category = "Problems", Thread = TestThread.Background)]
    public static void Scopes_do_not_disturb_each_other()
    {
        // Rebuilding one chunk must not clear another's problems, which is the whole reason scopes
        // are per chunk rather than per system.
        var problems = new ProblemSystem();
        problems.Replace("chunk:0,0", [Make("a")]);
        problems.Replace("chunk:1,0", [Make("b")]);

        problems.Replace("chunk:0,0", []);

        Assert.AreEqual(1, problems.Count);
        Assert.AreEqual("b", problems.All.Single().Key);
    }

    [EditorTest(Category = "Problems", Thread = TestThread.Background)]
    public static void The_same_problem_reported_twice_is_one_problem()
    {
        var problems = new ProblemSystem();
        problems.Replace("chunk:0,0", [Make("a"), Make("a"), Make("b")]);

        Assert.AreEqual(2, problems.Count);
    }

    [EditorTest(Category = "Problems", Thread = TestThread.Background)]
    public static void Errors_sort_above_warnings()
    {
        var problems = new ProblemSystem();
        problems.Replace("scope", [Make("w", ProblemSeverity.Warning), Make("e"), Make("i", ProblemSeverity.Info)]);

        Assert.AreEqual(ProblemSeverity.Error, problems.All[0].Severity);
        Assert.AreEqual(ProblemSeverity.Info, problems.All[^1].Severity);
    }

    [EditorTest(Category = "Problems", Thread = TestThread.Background)]
    public static void Unloaded_scopes_can_be_dropped_in_bulk()
    {
        // What a chunk streaming out does: its problems describe terrain that is no longer there.
        var problems = new ProblemSystem();
        problems.Replace("landscape:chunk:0,0", [Make("a")]);
        problems.Replace("landscape:chunk:9,9", [Make("b")]);
        problems.Replace("landscape:catalog", [Make("c")]);

        problems.ClearWhere(scope => scope.StartsWith("landscape:chunk:") && scope != "landscape:chunk:0,0");

        Assert.AreEqual(2, problems.Count);
        Assert.IsFalse(problems.All.Any(problem => problem.Key == "b"));
        Assert.IsTrue(problems.All.Any(problem => problem.Key == "c"), "the catalog scope is untouched");
    }

    [EditorTest(Category = "Problems", Thread = TestThread.Background)]
    public static void Version_moves_only_when_something_actually_changes()
    {
        var problems = new ProblemSystem();
        problems.Replace("scope", [Make("a")]);
        int after = problems.Version;

        problems.Replace("empty", []);
        Assert.AreEqual(after, problems.Version, "clearing a scope that held nothing changes nothing");

        problems.ClearWhere(_ => false);
        Assert.AreEqual(after, problems.Version, "matching no scope changes nothing");

        problems.Replace("scope", []);
        Assert.Greater(problems.Version, after);
    }

    [EditorTest(Category = "Problems", Thread = TestThread.Background)]
    public static void Counting_by_severity_ignores_the_others()
    {
        var problems = new ProblemSystem();
        problems.Replace("scope", [Make("e1"), Make("e2"), Make("w", ProblemSeverity.Warning)]);

        Assert.AreEqual(2, problems.CountOf(ProblemSeverity.Error));
        Assert.AreEqual(1, problems.CountOf(ProblemSeverity.Warning));
        Assert.AreEqual(0, problems.CountOf(ProblemSeverity.Info));
    }
}
