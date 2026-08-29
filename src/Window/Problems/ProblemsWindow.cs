using System.Collections.Generic;
using System.Linq;
using Godot;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// Everything the editor wants to tell the user about, from any system.
///
/// The one thing this does beyond listing: it folds the many per-chunk problems one entity causes
/// into a single row. A road crossing forty chunks that loses its layer in three of them produces
/// three identical-looking errors, and reading them one at a time tells you nothing — whereas
/// "dropped in 3 of the 41 chunks it spans" says immediately that the road is <em>discontinuous</em>,
/// which is the thing the user actually sees and the hardest failure to diagnose from the terrain.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class ProblemsWindow : Window
{
    public override string? Category => "Debug";

    private static readonly NVector4 ErrorColour = new(1.0f, 0.45f, 0.4f, 1.0f);
    private static readonly NVector4 WarningColour = new(1.0f, 0.72f, 0.22f, 1.0f);
    private static readonly NVector4 InfoColour = new(0.6f, 0.75f, 0.9f, 1.0f);

    private readonly EditorContext _context;

    private bool _showWarnings = true;
    private bool _showInfo = true;

    public ProblemsWindow(WindowManager manager)
        : base("Problems", startOpen: false, defaultSize: new NVector2(560.0f, 360.0f))
    {
        _context = manager.Context;
    }

    /// <summary>One row: either a single problem, or every occurrence of one entity's fault.</summary>
    private sealed record Row(
        Problem Representative,
        int Occurrences,
        int? SpannedChunks)
    {
        public bool IsAggregate => Occurrences > 1;

        /// <summary>
        /// True when a fault hits some of the chunks an entity spans but not all of them — the
        /// signature of terrain that stops and restarts partway along.
        /// </summary>
        public bool IsDiscontinuous => SpannedChunks is { } spanned && Occurrences < spanned;
    }

    protected override void DrawContent()
    {
        IReadOnlyList<Problem> problems = _context.Problems.All;

        DrawToolbar(problems);
        ImGui.Separator();

        List<Row> rows = Aggregate(problems.Where(Visible).ToList());
        if (rows.Count == 0)
        {
            ImGui.TextColored(new NVector4(0.42f, 0.85f, 0.46f, 1.0f), "Nothing to report.");
            return;
        }

        foreach (IGrouping<string, Row> byCategory in rows.GroupBy(row => row.Representative.Category))
        {
            if (!ImGui.CollapsingHeader($"{byCategory.Key} ({byCategory.Count()})", ImGuiTreeNodeFlags.DefaultOpen))
            {
                continue;
            }

            foreach (Row row in byCategory)
            {
                DrawRow(row);
            }
        }
    }

    private void DrawToolbar(IReadOnlyList<Problem> problems)
    {
        int errors = problems.Count(problem => problem.Severity == ProblemSeverity.Error);
        int warnings = problems.Count(problem => problem.Severity == ProblemSeverity.Warning);

        ImGui.TextColored(errors > 0 ? ErrorColour : InfoColour, $"{errors} errors");
        ImGui.SameLine();
        ImGui.TextColored(warnings > 0 ? WarningColour : InfoColour, $"{warnings} warnings");

        ImGui.SameLine();
        ImGui.Checkbox("Warnings", ref _showWarnings);
        ImGui.SameLine();
        ImGui.Checkbox("Info", ref _showInfo);
    }

    private bool Visible(Problem problem) => problem.Severity switch
    {
        ProblemSeverity.Warning => _showWarnings,
        ProblemSeverity.Info => _showInfo,
        _ => true,
    };

    private void DrawRow(Row row)
    {
        Problem problem = row.Representative;
        ImGui.PushID(problem.Key);

        NVector4 colour = problem.Severity switch
        {
            ProblemSeverity.Error => ErrorColour,
            ProblemSeverity.Warning => WarningColour,
            _ => InfoColour,
        };

        string label = row.IsAggregate ? Summarize(row) : problem.Message;
        ImGui.TextColored(colour, "●");
        ImGui.SameLine();

        if (ImGui.Selectable(label))
        {
            Navigate(problem);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(row.IsAggregate ? problem.Message : $"{problem.Kind}\nClick to select and look at it.");
        }

        ImGui.PopID();
    }

    private static string Summarize(Row row)
    {
        string what = DescribeEntity(row.Representative);
        return row.IsDiscontinuous
            ? $"{what}: {row.Representative.Kind} in {row.Occurrences} of the {row.SpannedChunks} chunks it spans — discontinuous"
            : $"{what}: {row.Representative.Kind} in {row.Occurrences} chunks";
    }

    private static string DescribeEntity(Problem problem) => problem.Entity is { } id ? $"Entity {id.Value}" : "Several chunks";

    /// <summary>
    /// Folds problems of the same kind caused by the same entity into one row, and works out how many
    /// chunks that entity spans so a partial failure can be named as such.
    /// </summary>
    private List<Row> Aggregate(IReadOnlyList<Problem> problems)
    {
        var rows = new List<Row>();

        foreach (IGrouping<(EntityId?, string, string), Problem> group in problems.GroupBy(
                     problem => (problem.Entity, problem.Category, problem.Kind)))
        {
            List<Problem> members = group.ToList();
            if (members.Count == 1 || group.Key.Item1 is null)
            {
                // Nothing to fold: without an entity, "the same kind in several chunks" is not
                // necessarily one fault, so listing them separately is the honest thing.
                rows.AddRange(members.Select(problem => new Row(problem, 1, null)));
                continue;
            }

            rows.Add(new Row(members[0], members.Count, SpannedChunks(group.Key.Item1.Value)));
        }

        return rows
            .OrderByDescending(row => row.Representative.Severity)
            .ThenByDescending(row => row.IsDiscontinuous)
            .ThenBy(row => row.Representative.Message, System.StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>How many chunks an entity's influence covers, or null if it is not a deformer.</summary>
    private int? SpannedChunks(EntityId id)
    {
        if (_context.Landscape.Grid is not { } grid)
        {
            return null;
        }

        foreach (SceneEntity entity in _context.Scene.Entities)
        {
            if (!entity.Id.Equals(id))
            {
                continue;
            }

            Aabb? bounds = null;
            foreach (ILandscapeDeformer deformer in entity.Components.OfType<ILandscapeDeformer>())
            {
                bounds = bounds is { } existing ? existing.Merge(deformer.InfluenceBounds) : deformer.InfluenceBounds;
            }

            return bounds is { } influence ? grid.Overlapping(influence).Count() : null;
        }

        return null;
    }

    private void Navigate(Problem problem)
    {
        if (problem.Entity is { } id)
        {
            foreach (SceneEntity entity in _context.Scene.Entities)
            {
                if (entity.Id.Equals(id))
                {
                    _context.Selection.Set(entity);
                    _context.Focus.LookAt(entity.Transform.Origin);
                    return;
                }
            }
        }

        if (problem.Location is { } location)
        {
            _context.Focus.LookAt(location);
        }
    }
}
