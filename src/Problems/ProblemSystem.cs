using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Collects problems from anywhere in the editor, grouped into scopes that can be replaced
/// independently.
///
/// A <b>scope</b> is whatever unit of state gets recomputed together — one landscape chunk, one
/// catalog. Replacing a scope is what makes fixed problems vanish: the rebuild that produced them
/// runs again and simply reports fewer, without anyone tracking which individual problem went away.
/// Getting that wrong in the other direction is the usual failure: a problem list that only ever
/// grows, which people learn to ignore.
///
/// Reports arrive from background builds, so this is guarded.
/// </summary>
public sealed class ProblemSystem
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<Problem>> _byScope = [];

    /// <summary>Bumps whenever anything changes, so views know when to refresh.</summary>
    public int Version { get; private set; }

    /// <summary>Every problem, worst first, then by category and message.</summary>
    public IReadOnlyList<Problem> All
    {
        get
        {
            lock (_lock)
            {
                return _byScope.Values
                    .SelectMany(problems => problems)
                    .OrderByDescending(problem => problem.Severity)
                    .ThenBy(problem => problem.Category, StringComparer.Ordinal)
                    .ThenBy(problem => problem.Message, StringComparer.Ordinal)
                    .ToList();
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _byScope.Values.Sum(problems => problems.Count);
            }
        }
    }

    public int CountOf(ProblemSeverity severity)
    {
        lock (_lock)
        {
            return _byScope.Values.Sum(problems => problems.Count(problem => problem.Severity == severity));
        }
    }

    /// <summary>
    /// Replaces everything reported under a scope. An empty list clears it — which is how a chunk that
    /// rebuilt cleanly retracts what it said last time.
    /// </summary>
    public void Replace(string scope, IReadOnlyList<Problem> problems)
    {
        lock (_lock)
        {
            var deduped = new List<Problem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Problem problem in problems)
            {
                if (seen.Add(problem.Key))
                {
                    deduped.Add(problem);
                }
            }

            if (deduped.Count == 0)
            {
                if (!_byScope.Remove(scope))
                {
                    return;
                }
            }
            else
            {
                _byScope[scope] = deduped;
            }

            Version++;
        }
    }

    /// <summary>Drops every scope matching a predicate, e.g. the chunks that just unloaded.</summary>
    public void ClearWhere(Func<string, bool> matches)
    {
        lock (_lock)
        {
            List<string> gone = _byScope.Keys.Where(matches).ToList();
            if (gone.Count == 0)
            {
                return;
            }

            foreach (string scope in gone)
            {
                _byScope.Remove(scope);
            }

            Version++;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (_byScope.Count == 0)
            {
                return;
            }

            _byScope.Clear();
            Version++;
        }
    }
}
