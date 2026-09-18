#nullable enable
using System;
using System.Text.RegularExpressions;

namespace WorldMapStudio;

/// <summary>
/// Matches test ids (<c>Category.Name</c>) against a user-typed filter: a case-insensitive substring,
/// or, when the filter contains <c>*</c> or <c>?</c>, a case-insensitive glob over the whole id.
/// Parsed once so a per-frame UI filter or a large test list does not rebuild the pattern per test.
/// </summary>
public sealed class TestFilter
{
    private readonly string _text;
    private readonly Regex? _glob;

    private TestFilter(string text, Regex? glob)
    {
        _text = text;
        _glob = glob;
    }

    /// <summary>Matches every id.</summary>
    public static TestFilter All { get; } = new("", null);

    public bool IsEmpty => _text.Length == 0;

    public static TestFilter Parse(string? filter)
    {
        string text = filter?.Trim() ?? "";
        if (text.Length == 0)
        {
            return All;
        }

        if (text.IndexOfAny(new[] { '*', '?' }) < 0)
        {
            return new TestFilter(text, null);
        }

        string pattern = "^" + Regex.Escape(text).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return new TestFilter(text, new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline));
    }

    public bool Matches(string id)
    {
        if (_text.Length == 0)
        {
            return true;
        }

        return _glob is not null
            ? _glob.IsMatch(id)
            : id.Contains(_text, StringComparison.OrdinalIgnoreCase);
    }
}
