namespace WorldMapStudio;

/// <summary>The editor's day clock, exposed to JS as <c>wms.clock</c>.</summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class ClockScriptApi : IScriptModule
{
    private readonly WorldClock _clock;

    public string Name => "clock";

    public float Priority => 0f;

    public ClockScriptApi(ScriptingSystem system)
    {
        _clock = system.Context.Clock;
    }

    /// <summary>0..1 fraction of a day.</summary>
    [ScriptFunction]
    public double GetDayFraction() => _clock.DayFraction;

    /// <summary>Sets the time of day directly, as a 0..1 fraction.</summary>
    [ScriptFunction]
    public void SetDayFraction(double value) => _clock.DayFraction = (float)value;

    [ScriptFunction]
    public bool IsPlaying() => _clock.Playing;

    [ScriptFunction]
    public void SetPlaying(bool playing) => _clock.Playing = playing;

    /// <summary>How many real seconds one full day takes while playing.</summary>
    [ScriptFunction]
    public double GetSecondsPerDay() => _clock.SecondsPerDay;

    [ScriptFunction]
    public void SetSecondsPerDay(double value) => _clock.SecondsPerDay = (float)value;
}
