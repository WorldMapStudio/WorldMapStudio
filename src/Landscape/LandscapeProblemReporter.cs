using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Translates what the landscape builder found into the editor's general problem list.
///
/// Scopes are per chunk, because a chunk is what gets rebuilt: when one resolves cleanly it replaces
/// its scope with nothing, and whatever it reported last time disappears. Chunks that unload have
/// their scope dropped, so problems never outlive the terrain they describe.
/// </summary>
public sealed class LandscapeProblemReporter
{
    public const string Category = "Landscape";

    private const string ChunkPrefix = "landscape:chunk:";
    private const string CatalogScope = "landscape:catalog";

    private readonly ProblemSystem _problems;

    public LandscapeProblemReporter(ProblemSystem problems)
    {
        _problems = problems;
    }

    /// <summary>
    /// Records what a build found. Every chunk it built gets its scope replaced — including the ones
    /// with nothing wrong, which is what clears problems the user has just fixed.
    /// </summary>
    public void Report(LandscapeBuildResult result, LandscapeGrid grid, IReadOnlyList<ILandscapeDeformer> deformers)
    {
        Dictionary<string, EntityId> sources = deformers
            .Select(deformer => (Deformer: deformer, Owner: (deformer as SceneComponent)?.Owner))
            .Where(pair => pair.Owner != null)
            .ToDictionary(pair => pair.Deformer.DeformerKey, pair => pair.Owner!.Id);

        foreach (ChunkCoord coord in result.Chunks.Keys)
        {
            List<Problem> problems = result.Problems
                .Where(entry => entry.Coord == coord)
                .Select(entry => ToProblem(entry.Coord, entry.Problem, grid, sources))
                .ToList();

            _problems.Replace(ScopeOf(coord), problems);
        }
    }

    /// <summary>Records what is wrong with the authored catalog, independent of any chunk.</summary>
    public void ReportCatalog(IReadOnlyList<LandscapeIssue> issues)
    {
        List<Problem> problems = issues.Select(issue => new Problem
        {
            Key = $"catalog:{issue.Message}",
            Severity = issue.Severity == LandscapeIssueSeverity.Error ? ProblemSeverity.Error : ProblemSeverity.Warning,
            Category = Category,
            Kind = "Catalog",
            Message = issue.Message,
        }).ToList();

        _problems.Replace(CatalogScope, problems);
    }

    /// <summary>Drops the problems of chunks that are no longer loaded.</summary>
    public void KeepOnly(IReadOnlyCollection<ChunkCoord> loaded)
    {
        var keep = new HashSet<string>(loaded.Select(ScopeOf));
        _problems.ClearWhere(scope => scope.StartsWith(ChunkPrefix, System.StringComparison.Ordinal) && !keep.Contains(scope));
    }

    /// <summary>Forgets every landscape problem, e.g. on entering another map.</summary>
    public void ClearAll() =>
        _problems.ClearWhere(scope =>
            scope.StartsWith(ChunkPrefix, System.StringComparison.Ordinal) || scope == CatalogScope);

    private static string ScopeOf(ChunkCoord coord) => $"{ChunkPrefix}{coord}";

    private static Problem ToProblem(
        ChunkCoord coord,
        LandscapeProblem problem,
        LandscapeGrid grid,
        IReadOnlyDictionary<string, EntityId> sources)
    {
        // The first group named is the one to blame; a conflict lists its losers, so pointing at the
        // first is pointing at whoever needs changing.
        EntityId? entity = problem.GroupKeys
            .Select(key => sources.TryGetValue(key, out EntityId id) ? id : (EntityId?)null)
            .FirstOrDefault(id => id != null);

        Vector3 origin = grid.OriginOf(coord);
        float half = grid.ChunkSize * 0.5f;

        return new Problem
        {
            // Chunk plus kind plus the groups involved: the same fault found again on a rebuild is
            // the same problem, not a new one.
            Key = $"{coord}:{problem.Kind}:{string.Join(",", problem.GroupKeys)}",
            Severity = SeverityOf(problem.Kind),
            Category = Category,
            Kind = problem.Kind.ToString(),
            Message = $"[{coord}] {problem.Message}",
            Entity = entity,
            Location = new Vector3(origin.X + half, origin.Y, origin.Z + half),
        };
    }

    private static ProblemSeverity SeverityOf(LandscapeProblemKind kind) => kind switch
    {
        // The budget working as designed: the user asked for more than the format allows and the
        // resolver picked. Worth surfacing, but the terrain is valid.
        LandscapeProblemKind.BudgetOverflow => ProblemSeverity.Warning,

        // A binding that quietly does nothing — wrong, but only for that contribution.
        LandscapeProblemKind.MissingHeightFunction => ProblemSeverity.Warning,
        LandscapeProblemKind.MissingAlphaFunction => ProblemSeverity.Warning,
        LandscapeProblemKind.MissingHoleFunction => ProblemSeverity.Warning,
        LandscapeProblemKind.MissingVertexColorFunction => ProblemSeverity.Warning,
        LandscapeProblemKind.MissingVertexLightFunction => ProblemSeverity.Warning,
        LandscapeProblemKind.MissingAttributeFunction => ProblemSeverity.Warning,

        // The value was masked to fit rather than dropped, so the build is still usable — but it is
        // almost certainly not what the author meant.
        LandscapeProblemKind.AttributeValueOverflow => ProblemSeverity.Warning,

        _ => ProblemSeverity.Error,
    };
}
