using System;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Gates a window's field rows on a "filter fields" search string: a row draws only when its label
/// matches, a <see cref="Group"/> shows just its matching rows (or nothing, header included, when none
/// match), and <see cref="Separator"/>s drop out so a filtered list doesn't sprout floating rules.
/// Knows nothing about widgets, entities or edit tracking — the caller supplies the draw callback and
/// keeps the filter string across frames (see <see cref="ImGuiEx.FieldFilterInput"/>), building one of
/// these per frame from it.
///
/// Inside a <see cref="Group"/> body, everything must go through <see cref="Field(string, Action)"/>,
/// <see cref="Separator"/>, <see cref="Chrome"/> or a nested <see cref="Group"/> — a bare ImGui call
/// there would be drawn again by the group's measure pass.
/// </summary>
public sealed class FieldFilter
{
    private readonly string[] _tokens;

    // Set while a Group body runs its measure pass: Field calls tally matches and draw nothing.
    private bool _measuring;
    private int _measureHits;

    // Set while a matched-by-title Group's body runs for real: every row shows regardless of label.
    private bool _groupShowAll;

    // Prefixed onto row labels when matching so typing a group's name reveals the whole group.
    private string? _groupPrefix;

    public FieldFilter(string filterText)
    {
        _tokens = string.IsNullOrWhiteSpace(filterText)
            ? []
            : filterText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    public bool Filtering => _tokens.Length > 0;

    /// <summary>The gate a field row runs first: <c>false</c> = don't draw it. In a <see cref="Group"/>
    /// measure pass it only tallies and always returns <c>false</c>.</summary>
    public bool Field(string label)
    {
        if (_measuring)
        {
            if (_groupShowAll || Matches(Prefixed(label)))
            {
                _measureHits++;
            }

            return false;
        }

        return _groupShowAll || !Filtering || Matches(Prefixed(label));
    }

    /// <summary>Runs <paramref name="draw"/> when a row labelled <paramref name="label"/> passes the
    /// filter — for one widget or a whole bespoke block treated as a unit.</summary>
    public void Field(string label, Action draw)
    {
        if (Field(label))
        {
            draw();
        }
    }

    /// <summary>A rule between field groups. Suppressed while a filter is active.</summary>
    public void Separator()
    {
        if (_measuring || Filtering)
        {
            return;
        }

        ImGui.Separator();
    }

    /// <summary>
    /// Section chrome — a heading label, a Remove button, a status line — that should draw whenever
    /// its section does, regardless of the filter. Skipped during a <see cref="Group"/> measure pass
    /// so it isn't drawn twice; use this rather than a bare ImGui call inside a group body.
    /// </summary>
    public void Chrome(Action draw)
    {
        if (!_measuring)
        {
            draw();
        }
    }

    /// <summary>
    /// A collapsible section. Unfiltered it's a plain collapsing header (open by default when
    /// <paramref name="defaultOpen"/>). Filtered it is force-open and drawn only when
    /// <paramref name="title"/> or one of its rows matches — matching the title reveals every row.
    /// Reentrant: a nested group's title stacks onto the prefix, and its matches count towards the
    /// parent being shown.
    /// </summary>
    public void Group(string title, Action body, bool defaultOpen = false)
    {
        string prefixedTitle = Prefixed(title);

        if (_measuring)
        {
            // Nested inside a measuring parent — contribute upward. A title match reveals the whole
            // subtree, so it counts as one hit without descending.
            _measureHits += Matches(prefixedTitle) ? 1 : RunMeasured(prefixedTitle, body);
            return;
        }

        if (!Filtering)
        {
            if (defaultOpen)
            {
                ImGui.SetNextItemOpen(true, ImGuiCond.FirstUseEver);
            }

            if (ImGui.CollapsingHeader(title))
            {
                Enter(prefixedTitle, showAll: false, body);
            }

            return;
        }

        bool titleMatches = Matches(prefixedTitle);
        if (!titleMatches && !_groupShowAll && RunMeasured(prefixedTitle, body) == 0)
        {
            return;
        }

        ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        if (ImGui.CollapsingHeader(title))
        {
            Enter(prefixedTitle, titleMatches || _groupShowAll, body);
        }
    }

    // Runs body with the group's prefix/show-all state pushed, then restores it.
    private void Enter(string prefix, bool showAll, Action body)
    {
        string? prevPrefix = _groupPrefix;
        bool prevShowAll = _groupShowAll;

        _groupPrefix = prefix;
        _groupShowAll = showAll;
        body();
        _groupShowAll = prevShowAll;
        _groupPrefix = prevPrefix;
    }

    // Runs body in measure mode under the given label prefix; returns how many rows matched.
    private int RunMeasured(string prefix, Action body)
    {
        bool prevMeasuring = _measuring;
        int prevHits = _measureHits;
        string? prevPrefix = _groupPrefix;

        _measuring = true;
        _measureHits = 0;
        _groupPrefix = prefix;

        body();
        int hits = _measureHits;

        _measuring = prevMeasuring;
        _measureHits = prevHits;
        _groupPrefix = prevPrefix;
        return hits;
    }

    private string Prefixed(string label) => _groupPrefix is null ? label : $"{_groupPrefix} {label}";

    private bool Matches(string haystack)
    {
        foreach (string token in _tokens)
        {
            if (haystack.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        return true;
    }
}
