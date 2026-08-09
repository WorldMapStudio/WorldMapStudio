using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// What went wrong resolving a chunk. These are normal operating conditions in a system with a fixed
/// texture budget, not exceptions — the point of the design is that they are <em>reported</em> rather
/// than silently clipped.
/// </summary>
public enum LandscapeProblemKind
{
    /// <summary>Two groups bound different materials to one layer. The layer is one responsibility.</summary>
    LayerConflict,

    /// <summary>A group was dropped whole to fit the texture limit.</summary>
    BudgetOverflow,

    /// <summary>More than one base layer was claimed; a chunk has exactly one opaque bottom.</summary>
    MultipleBaseLayers,

    /// <summary>Nothing claimed a base and the map has no fallback material, so the chunk is a hole.</summary>
    MissingBase,

    /// <summary>A texture claim carried no material, so there was nothing to put in the slot.</summary>
    MissingMaterial,

    /// <summary>Even dropping every droppable group left more slots in use than the limit allows.</summary>
    Unsatisfiable,
}

/// <summary>
/// One reported problem. Chunk-free on purpose: the resolver works on a single chunk's claims and
/// does not know where it is, so the builder attaches the coordinate when it collects these.
/// </summary>
public sealed record LandscapeProblem(
    LandscapeProblemKind Kind,
    string Message,
    IReadOnlyList<string> GroupKeys)
{
    public static LandscapeProblem Create(LandscapeProblemKind kind, string message, params string[] groupKeys) =>
        new(kind, message, groupKeys);

    public override string ToString() => $"{Kind}: {Message}";
}
