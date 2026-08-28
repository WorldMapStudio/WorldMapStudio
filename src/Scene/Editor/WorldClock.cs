using Godot;

namespace WorldMapStudio;

/// <summary>
/// A unit-free clock: <see cref="DayFraction"/> is 0..1 and wraps at the day boundary. A plugin maps
/// its own day representation onto this range (e.g. a 2880-unit day is <c>DayFraction * 2880</c>).
/// A plain member of <see cref="EditorContext"/>, advanced once per frame from <see cref="Editor.Update"/>
/// regardless of which windows are open, so time keeps flowing the same way the viewport's camera does.
/// </summary>
public sealed class WorldClock
{
    private const float MinSecondsPerDay = 1.0f;

    private float _dayFraction;
    private float _secondsPerDay = 300.0f;
    private ulong _lastTicksMsec;
    private bool _initialized;

    /// <summary>0 = start of day; wraps back to 0 at 1.</summary>
    public float DayFraction
    {
        get => _dayFraction;
        set => _dayFraction = Wrap(value);
    }

    public bool Playing { get; set; }

    /// <summary>How many real seconds one full day takes while playing.</summary>
    public float SecondsPerDay
    {
        get => _secondsPerDay;
        set => _secondsPerDay = Mathf.Max(MinSecondsPerDay, value);
    }

    /// <summary>Total real seconds this clock has advanced, unaffected by day wrap-around. Lets a
    /// source animate off real time instead of the day cycle (e.g. a slow-drifting cloud layer).</summary>
    public double Elapsed { get; private set; }

    /// <summary>
    /// Advances the clock by the real time elapsed since the last call. The first call after
    /// construction only primes the reference point and advances nothing, so a freshly opened project
    /// doesn't jump by however long startup took.
    /// </summary>
    public void Update()
    {
        ulong now = Time.GetTicksMsec();
        if (!_initialized)
        {
            _initialized = true;
            _lastTicksMsec = now;
            return;
        }

        double delta = (now - _lastTicksMsec) / 1000.0;
        _lastTicksMsec = now;

        if (!Playing || delta <= 0.0)
        {
            return;
        }

        Elapsed += delta;
        DayFraction = _dayFraction + (float)(delta / _secondsPerDay);
    }

    private static float Wrap(float value)
    {
        value %= 1.0f;
        return value < 0.0f ? value + 1.0f : value;
    }
}
