using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// One entity asking to occupy one layer in one chunk with one material. Claiming is pure and cheap:
/// no rasterization happens until the resolver has decided who survives, because the whole point is
/// that an entity is told the outcome before it writes anything.
/// </summary>
public sealed class LandscapeClaim
{
    public required LandscapeLayer Layer { get; init; }

    /// <summary>
    /// The material to bind. Required for texture layers, since that is what occupies the slot;
    /// height layers use it only to carry a height function, and may leave it null to claim the layer
    /// purely for conflict detection.
    /// </summary>
    public LandscapeTextureMaterial? Material { get; init; }

    public override string ToString() => $"{Layer.Name} = {Material?.Name ?? "(none)"}";
}

/// <summary>
/// Claims that stand or fall together. A road needing both a centre layer and a shoulder layer takes
/// both or neither: half a road is worse than no road, and the user said so by grouping them.
///
/// An entity that does not want atomicity emits one group per claim.
/// </summary>
public sealed class LandscapeClaimGroup
{
    /// <summary>
    /// Stable identity, and the resolver's tie-break. It must not depend on scan order or on runtime
    /// object identity, or two machines resolving the same chunk could drop different groups — which
    /// would show up as terrain that differs per machine. Build it from persistent keys, e.g.
    /// <c>"empty:42/road"</c>.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>Human-readable name used when reporting a conflict or a drop.</summary>
    public required string Label { get; init; }

    /// <summary>Higher survives when the texture budget is exceeded, or when two groups want one layer.</summary>
    public required int Priority { get; init; }

    public required IReadOnlyList<LandscapeClaim> Claims { get; init; }

    /// <summary>The entity that claimed, so a problem can navigate back to it. Optional.</summary>
    public EntityId? Source { get; init; }

    public override string ToString() => $"{Label} ({Key}, priority {Priority})";
}
