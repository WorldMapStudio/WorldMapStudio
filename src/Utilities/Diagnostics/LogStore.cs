using System;
using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// A bounded, newest-last list of <see cref="LogEntry"/>s fed one physical line at a time. An indented
/// line (Godot writes stack traces as <c>   at: …</c> under the message) folds into the previous
/// entry's detail rather than becoming a row of its own; every other non-empty line starts a new
/// entry, its level read from Godot's <c>ERROR:</c> / <c>WARNING:</c> prefixes.
/// </summary>
public sealed class LogStore
{
    private const int MaxDetail = 8 * 1024;

    private readonly int _capacity;
    private readonly List<LogEntry> _entries = [];
    private long _seq;
    private int _info;
    private int _warning;
    private int _error;

    public LogStore(int capacity = 5000) => _capacity = Math.Max(64, capacity);

    public IReadOnlyList<LogEntry> Entries => _entries;

    public (int Info, int Warning, int Error) Counts => (_info, _warning, _error);

    public void Clear()
    {
        _entries.Clear();
        _info = _warning = _error = 0;
    }

    public void Ingest(string line)
    {
        line = line.TrimEnd('\r');
        if (line.Length == 0)
        {
            return;
        }

        if ((line[0] == ' ' || line[0] == '\t') && _entries.Count > 0)
        {
            int last = _entries.Count - 1;
            LogEntry entry = _entries[last];
            string addition = line.TrimEnd();
            string detail = entry.Detail is null ? addition : $"{entry.Detail}\n{addition}";
            if (detail.Length > MaxDetail)
            {
                detail = detail[..MaxDetail] + "\n…";
            }

            _entries[last] = entry with { Detail = detail };
            return;
        }

        (LogLevel level, string message) = Classify(line);
        Append(new LogEntry(_seq++, DateTime.Now, level, message, null));
    }

    private void Append(LogEntry entry)
    {
        _entries.Add(entry);
        Count(entry.Level, 1);

        // Trimmed in slabs so a busy load isn't shifting the whole list on every line.
        if (_entries.Count > _capacity + 512)
        {
            int remove = _entries.Count - _capacity;
            for (int i = 0; i < remove; i++)
            {
                Count(_entries[i].Level, -1);
            }

            _entries.RemoveRange(0, remove);
        }
    }

    private void Count(LogLevel level, int delta)
    {
        switch (level)
        {
            case LogLevel.Warning:
                _warning += delta;
                break;
            case LogLevel.Error:
                _error += delta;
                break;
            default:
                _info += delta;
                break;
        }
    }

    private static (LogLevel Level, string Message) Classify(string line)
    {
        if (TryStrip(line, "ERROR: ", out string rest)
            || TryStrip(line, "SCRIPT ERROR: ", out rest)
            || TryStrip(line, "USER ERROR: ", out rest)
            || TryStrip(line, "USER SCRIPT ERROR: ", out rest))
        {
            return (LogLevel.Error, rest);
        }

        if (TryStrip(line, "WARNING: ", out rest)
            || TryStrip(line, "USER WARNING: ", out rest))
        {
            return (LogLevel.Warning, rest);
        }

        return (LogLevel.Info, line);
    }

    private static bool TryStrip(string line, string prefix, out string rest)
    {
        if (line.StartsWith(prefix, StringComparison.Ordinal))
        {
            rest = line[prefix.Length..];
            return true;
        }

        rest = line;
        return false;
    }
}
