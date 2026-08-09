using Godot;

namespace WorldMapStudio;

/// <summary>How much a problem matters.</summary>
public enum ProblemSeverity
{
    /// <summary>Worth knowing; nothing is broken.</summary>
    Info,

    /// <summary>Works, but probably not as intended.</summary>
    Warning,

    /// <summary>Cannot produce what was asked for.</summary>
    Error,
}

/// <summary>
/// One thing the editor wants to tell the user about, from any system.
///
/// Problems are <em>facts about the current state</em>, not events: they are replaced wholesale per
/// <see cref="ProblemScope"/> as the state that produced them is recomputed, so a fixed problem
/// disappears on its own rather than needing anyone to remember to retract it.
/// </summary>
public sealed record Problem
{
    /// <summary>
    /// Stable identity within its scope. Two reports of the same key are the same problem, so a
    /// rebuild that finds it again does not stack up duplicates.
    /// </summary>
    public required string Key { get; init; }

    public required ProblemSeverity Severity { get; init; }

    /// <summary>Which system is speaking, e.g. "Landscape". Groups the list.</summary>
    public required string Category { get; init; }

    /// <summary>What kind of problem this is, e.g. "BudgetOverflow". Aggregated on.</summary>
    public required string Kind { get; init; }

    public required string Message { get; init; }

    /// <summary>The entity responsible, when there is one. Selected on click.</summary>
    public EntityId? Entity { get; init; }

    /// <summary>Where in the world to look. The viewport moves here on click.</summary>
    public Vector3? Location { get; init; }

    public override string ToString() => $"{Severity}: {Message}";
}
