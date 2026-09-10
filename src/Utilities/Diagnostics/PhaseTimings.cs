using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Wall-clock time attributed to named phases of one long-running operation, accumulated from any
/// thread.
///
/// Wall clock rather than CPU, for the same reason <see cref="DiagnosticLog"/> exists alongside the
/// CPU sampler in <c>PerformanceWindow</c>: the phases that dominate a batch are awaited database
/// round-trips and main-thread hops that wait on a frame, neither of which a sampling trace can see.
///
/// Phases may nest, and a nested phase is counted in full against both itself and its parent — the
/// shares therefore sum to more than 100% and are read as "of the wall clock", not as a partition.
/// </summary>
public sealed class PhaseTimings
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly long _origin = Stopwatch.GetTimestamp();

    /// <summary>Seconds since this instance was created — the denominator every share is taken against.</summary>
    public double ElapsedSeconds => Stopwatch.GetElapsedTime(_origin).TotalSeconds;

    public bool IsEmpty
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count == 0;
            }
        }
    }

    /// <summary>Times a block: <c>using (timings.Measure("water"))</c>.</summary>
    public IDisposable Measure(string phase) => new Scope(this, phase);

    public void Add(string phase, TimeSpan elapsed, int count = 1)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(phase, out Entry? entry))
            {
                entry = new Entry();
                _entries[phase] = entry;
            }

            entry.Ticks += elapsed.Ticks;
            entry.Count += count;
        }
    }

    /// <summary>The breakdown as log-ready lines, slowest phase first. Empty when nothing measured
    /// reached <paramref name="minimumSeconds"/> — the threshold is what keeps a breakdown over a long
    /// participant list from burying the two entries that matter.</summary>
    public IReadOnlyList<string> Format(string title, double minimumSeconds = 0.0)
    {
        List<(string Phase, TimeSpan Elapsed, int Count)> rows;
        long measuredTicks;
        lock (_lock)
        {
            // Summed before the threshold filter, so a low bar for what is worth printing does not
            // turn every phase it hid into apparent unattributed time.
            measuredTicks = _entries.Values.Sum(entry => entry.Ticks);
            rows = _entries
                .Select(pair => (pair.Key, new TimeSpan(pair.Value.Ticks), pair.Value.Count))
                .Where(row => row.Item2.TotalSeconds >= minimumSeconds)
                .OrderByDescending(row => row.Item2)
                .ToList();
        }

        if (rows.Count == 0)
        {
            return [];
        }

        double wall = ElapsedSeconds;

        // Only meaningful while no phase nests inside another, which is why it is a remainder rather
        // than a row: an operation that nests can drive it negative, and a negative remainder is a
        // truthful "these phases overlap" rather than a number to trust.
        double measured = new TimeSpan(measuredTicks).TotalSeconds;
        double unattributed = wall - measured;

        int width = rows.Max(row => row.Phase.Length);
        var lines = new List<string>(rows.Count + 2)
        {
            string.Create(
                CultureInfo.InvariantCulture,
                $"{title} — {wall:F1}s wall, {measured:F1}s measured, {unattributed:F1}s outside any phase:"),
        };

        foreach ((string phase, TimeSpan elapsed, int count) in rows)
        {
            double seconds = elapsed.TotalSeconds;
            double share = wall > 0.0 ? seconds / wall * 100.0 : 0.0;
            double each = count > 0 ? elapsed.TotalMilliseconds / count : 0.0;
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"  {phase.PadRight(width)}  {seconds,9:F1}s  {share,5:F1}%  {count,7} call(s)  {each,9:F1}ms each"));
        }

        return lines;
    }

    private sealed class Entry
    {
        public long Ticks;
        public int Count;
    }

    private sealed class Scope : IDisposable
    {
        private readonly PhaseTimings _owner;
        private readonly string _phase;
        private readonly long _start;

        public Scope(PhaseTimings owner, string phase)
        {
            _owner = owner;
            _phase = phase;
            _start = Stopwatch.GetTimestamp();
        }

        public void Dispose() => _owner.Add(_phase, Stopwatch.GetElapsedTime(_start));
    }
}
