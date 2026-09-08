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
    /// A collapsible section. Unfiltered it's a plain collapsing header. Filtered it is force-open and
    /// drawn only when <paramref name="title"/> or one of its rows matches — matching the title reveals
    /// every row. Not reentrant: groups don't nest.
    /// </summary>
    public void Group(string title, Action body)
    {
        if (_measuring)
        {
            RunMeasured(title, body);
            return;
        }

        if (!Filtering)
        {
            if (ImGui.CollapsingHeader(title))
            {
                _groupPrefix = title;
                body();
                _groupPrefix = null;
            }

            return;
        }

        bool titleMatches = Matches(title);
        if (!titleMatches && RunMeasured(title, body) == 0)
        {
            return;
        }

        ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        if (ImGui.CollapsingHeader(title))
        {
            _groupPrefix = title;
            _groupShowAll = titleMatches;
            body();
            _groupShowAll = false;
            _groupPrefix = null;
        }
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
