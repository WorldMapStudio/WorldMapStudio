using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// One texture slot of a resolved chunk. Holds more than one layer when adjacent layers shared a
/// material and were merged into a single slot — the mitigation that buys a slot back without
/// changing what the chunk looks like.
/// </summary>
public sealed class LandscapeSlot
{
    public required int Index { get; init; }

    public required LandscapeMaterial Material { get; init; }

    public required bool IsBase { get; init; }

    /// <summary>The layers occupying this slot, in draw order. More than one means they were merged.</summary>
    public required IReadOnlyList<LandscapeLayer> Layers { get; init; }

    public bool IsMerged => Layers.Count > 1;

    public override string ToString() =>
        $"[{Index}] {Material.Name}{(IsBase ? " (base)" : "")} ← {string.Join(" + ", Layers.Select(l => l.Name))}";
}

/// <summary>
/// What a chunk resolved to: which materials occupy which slots, which height layers run and in what
/// order, which groups were dropped, and everything that went wrong doing it.
///
/// This is the object handed back to entities before they rasterize, so each can see whether its
/// claims won, were merged, or were dropped — and decide what to write accordingly.
/// </summary>
public sealed class LandscapeResolution
{
    public required LandscapeSlot? Base { get; init; }

    /// <summary>Alpha slots in compositing order, over the base.</summary>
    public required IReadOnlyList<LandscapeSlot> AlphaSlots { get; init; }

    /// <summary>Surviving height claims in evaluation order — the layer draw order, never scan order.</summary>
    public required IReadOnlyList<LandscapeClaim> HeightClaims { get; init; }

    /// <summary>Keys of the groups that did not survive, in the order they were dropped.</summary>
    public required IReadOnlyList<string> DroppedGroups { get; init; }

    public required IReadOnlyList<LandscapeProblem> Problems { get; init; }

    /// <summary>Texture slots used, base included — what the map's texture limit constrains.</summary>
    public int UsedSlots => (Base == null ? 0 : 1) + AlphaSlots.Count;

    public IEnumerable<LandscapeSlot> Slots => Base == null ? AlphaSlots : AlphaSlots.Prepend(Base);

    public bool IsClean => Problems.Count == 0;

    /// <summary>Whether the group with this key was dropped, so its entity can react while rasterizing.</summary>
    public bool IsDropped(string groupKey) => DroppedGroups.Contains(groupKey);

    /// <summary>The slot a surviving layer landed in, or null if it did not survive.</summary>
    public int? SlotOf(LandscapeLayer layer)
    {
        foreach (LandscapeSlot slot in Slots)
        {
            if (slot.Layers.Contains(layer))
            {
                return slot.Index;
            }
        }

        return null;
    }
}
