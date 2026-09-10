using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Turns a chunk's claims into a slot assignment that fits the map's texture limit, reporting
/// everything it had to give up doing so.
///
/// What a claim costs is a property of the <em>material</em> bound to it, not of the layer: a claim
/// occupies a texture slot when its material paints one, and contributes height when its material
/// deforms. A layer says only who is responsible and in what order, so one claim can do both.
///
/// Deliberately <b>greedy and deterministic</b>, not optimal. A cleverer search could sometimes keep
/// one more layer, but it would also pick different survivors in adjacent chunks — which is exactly
/// how you manufacture visible seams. Predictable beats optimal here, so ordering is by explicit keys
/// throughout and never by input order.
///
/// Pure: no I/O, no Godot, no channels. It runs the same on any thread and any machine.
/// </summary>
public static class LandscapeResolver
{
    public static LandscapeResolution Resolve(
        IReadOnlyList<LandscapeClaimGroup> groups,
        LandscapeSettings settings,
        LandscapeCatalog catalog)
    {
        var problems = new List<LandscapeProblem>();
        var dropped = new List<string>();

        // Canonical order up front, so a shuffled scan resolves identically.
        List<LandscapeClaimGroup> live = groups
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToList();

        RejectMaterialless(live, problems);

        while (true)
        {
            if (Kill(live, dropped, ConflictLosers(live, problems)))
            {
                continue;
            }

            if (Kill(live, dropped, BaseLosers(live, problems)))
            {
                continue;
            }

            LandscapeResolution resolution = Build(live, settings, catalog, dropped, problems);
            if (resolution.UsedSlots <= settings.TextureLimit)
            {
                return resolution;
            }

            if (LowestPriorityHolder(live) is not { } victim)
            {
                // Nothing left that frees a slot: the limit is below what a single base plus the
                // undroppable remainder needs. Rebuild rather than returning the resolution above —
                // it snapshotted the problem list before this was known.
                problems.Add(LandscapeProblem.Create(
                    LandscapeProblemKind.Unsatisfiable,
                    $"{resolution.UsedSlots} slots are needed but the limit is {settings.TextureLimit}."));
                return Build(live, settings, catalog, dropped, problems);
            }

            problems.Add(LandscapeProblem.Create(
                LandscapeProblemKind.BudgetOverflow,
                $"'{victim.Label}' was dropped to fit {settings.TextureLimit} textures.",
                victim.Key));
            Kill(live, dropped, [victim]);
        }
    }

    // A claim with no material asked for a layer and supplied nothing to put in it. Reported once,
    // then ignored — the group survives, since its other claims may be fine.
    private static void RejectMaterialless(List<LandscapeClaimGroup> live, List<LandscapeProblem> problems)
    {
        for (int i = 0; i < live.Count; i++)
        {
            LandscapeClaimGroup group = live[i];
            List<LandscapeClaim> usable = group.Claims.Where(claim => claim.Material != null).ToList();

            if (usable.Count == group.Claims.Count)
            {
                continue;
            }

            foreach (LandscapeClaim rejected in group.Claims.Except(usable))
            {
                problems.Add(LandscapeProblem.Create(
                    LandscapeProblemKind.MissingMaterial,
                    $"'{group.Label}' claimed layer '{rejected.Layer.Name}' without a material.",
                    group.Key));
            }

            live[i] = Without(group, usable);
        }
    }

    /// <summary>
    /// Two groups binding different materials to one layer is the conflict a layer exists to catch:
    /// the layer is one responsibility, so there is no meaningful way to honour both. Applies to every
    /// claim, including ones that only contribute height — two entities disagreeing about how the
    /// ground under a road is shaped is the same kind of mistake as disagreeing about its texture.
    /// </summary>
    private static List<LandscapeClaimGroup> ConflictLosers(
        List<LandscapeClaimGroup> live,
        List<LandscapeProblem> problems)
    {
        var losers = new List<LandscapeClaimGroup>();

        foreach (IGrouping<LandscapeLayer, Entry> byLayer in Entries(live).GroupBy(entry => entry.Claim.Layer))
        {
            List<Entry> contenders = byLayer.ToList();
            if (contenders.Select(entry => entry.Claim.Material).Distinct().Count() <= 1)
            {
                continue;
            }

            Entry winner = contenders.OrderBy(Rank).Last();
            List<LandscapeClaimGroup> beaten = contenders
                .Where(entry => entry.Group != winner.Group)
                .Select(entry => entry.Group)
                .Distinct()
                .ToList();

            problems.Add(new LandscapeProblem(
                LandscapeProblemKind.LayerConflict,
                $"Layer '{byLayer.Key.Name}' was claimed with different materials; " +
                $"'{winner.Group.Label}' won with '{winner.Claim.Material!.Name}'.",
                beaten.Select(group => group.Key).ToList()));

            losers.AddRange(beaten);
        }

        return losers;
    }

    // A base layer is the chunk's opaque bottom, so exactly one may be bound.
    private static List<LandscapeClaimGroup> BaseLosers(
        List<LandscapeClaimGroup> live,
        List<LandscapeProblem> problems)
    {
        List<Entry> bases = Entries(live).Where(entry => entry.Claim.Layer.IsBase).ToList();

        if (bases.Select(entry => entry.Claim.Layer).Distinct().Count() <= 1)
        {
            return [];
        }

        Entry winner = bases.OrderBy(Rank).Last();
        List<LandscapeClaimGroup> beaten = bases
            .Where(entry => entry.Group != winner.Group)
            .Select(entry => entry.Group)
            .Distinct()
            .ToList();

        problems.Add(new LandscapeProblem(
            LandscapeProblemKind.MultipleBaseLayers,
            $"More than one base layer was claimed; '{winner.Claim.Layer.Name}' won.",
            beaten.Select(group => group.Key).ToList()));

        return beaten;
    }

    private static LandscapeResolution Build(
        List<LandscapeClaimGroup> live,
        LandscapeSettings settings,
        LandscapeCatalog catalog,
        List<string> dropped,
        List<LandscapeProblem> problems)
    {
        List<LandscapeClaim> ordered = Entries(live)
            .Select(entry => entry.Claim)
            .OrderBy(claim => claim.Layer.DrawOrder)
            .ToList();

        // A claim costs a slot when its material paints one — the layer has no say in it.
        List<LandscapeClaim> painting = ordered.Where(claim => claim.Material!.PaintsTexture).ToList();

        LandscapeSlot? baseSlot = BuildBase(painting, settings, catalog, problems);
        int nextIndex = baseSlot == null ? 0 : 1;

        var alphaSlots = new List<LandscapeSlot>();
        foreach (LandscapeClaim claim in painting.Where(claim => !claim.Layer.IsBase))
        {
            if (baseSlot is { Layers.Count: > 0 } && claim.Layer.DrawOrder < baseSlot.Layers[0].DrawOrder)
            {
                AddOnce(problems, LandscapeProblem.Create(
                    LandscapeProblemKind.BelowBase,
                    $"Layer '{claim.Layer.Name}' sorts below the base layer '{baseSlot.Layers[0].Name}', " +
                    "so it would be painted over and never seen."));
            }

            // Merge only into the slot immediately below: adjacency is what makes a merge exact,
            // and it is adjacency in the *surviving* order, which is why this runs after every drop.
            LandscapeSlot? previous = alphaSlots.Count > 0 ? alphaSlots[^1] : null;
            if (previous != null && ReferenceEquals(previous.Material, claim.Material))
            {
                alphaSlots[^1] = new LandscapeSlot
                {
                    Index = previous.Index,
                    Material = previous.Material,
                    IsBase = false,
                    Layers = previous.Layers.Append(claim.Layer).ToList(),
                };
                continue;
            }

            alphaSlots.Add(new LandscapeSlot
            {
                Index = nextIndex++,
                Material = claim.Material!,
                IsBase = false,
                Layers = [claim.Layer],
            });
        }

        // Height is independent of the slot budget, so a claim can contribute height whether or not it
        // also won a texture slot.
        List<LandscapeClaim> heights = ordered.Where(claim => claim.Material!.DeformsHeight).ToList();

        // Holes are independent of the slot budget too, and unlike height the claims never even need
        // to be ordered against each other — cutting a hole is a union, not a transform.
        List<LandscapeClaim> holes = ordered.Where(claim => claim.Material!.CutsHole).ToList();

        // Vertex color and vertex light are independent of the slot budget too, and share height's
        // accumulate-and-transform shape, so they are ordered the same way.
        List<LandscapeClaim> vertexColors = ordered.Where(claim => claim.Material!.PaintsVertexColor).ToList();
        List<LandscapeClaim> vertexLight = ordered.Where(claim => claim.Material!.PaintsVertexLight).ToList();

        // Whether a claim writes an attribute is a property of the map's declared writes, not of the
        // material entity — so it is asked of the catalog, unlike the five built-in output kinds.
        List<LandscapeClaim> attributes = ordered.Where(claim => catalog.WritesAttributes(claim.Material!)).ToList();

        return new LandscapeResolution
        {
            Base = baseSlot,
            AlphaSlots = alphaSlots,
            HeightClaims = heights,
            HoleClaims = holes,
            VertexColorClaims = vertexColors,
            VertexLightClaims = vertexLight,
            AttributeClaims = attributes,
            DroppedGroups = dropped.ToList(),
            Problems = problems.ToList(),
        };
    }

    private static LandscapeSlot? BuildBase(
        List<LandscapeClaim> painting,
        LandscapeSettings settings,
        LandscapeCatalog catalog,
        List<LandscapeProblem> problems)
    {
        if (painting.FirstOrDefault(claim => claim.Layer.IsBase) is { } claimed)
        {
            return new LandscapeSlot
            {
                Index = 0,
                Material = claimed.Material!,
                IsBase = true,
                Layers = [claimed.Layer],
            };
        }

        // Nothing claimed a base, so the map's fallback keeps the chunk from being a hole.
        LandscapeMaterial? fallback = settings.FallbackMaterialId is { } id
            ? catalog.Materials.FirstOrDefault(material => material.RecordId == id)
            : null;

        if (fallback == null)
        {
            AddOnce(problems, LandscapeProblem.Create(
                LandscapeProblemKind.MissingBase,
                "No base layer was claimed and the map has no fallback material."));
            return null;
        }

        return new LandscapeSlot
        {
            Index = 0,
            Material = fallback,
            IsBase = true,
            Layers = [],
        };
    }

    /// <summary>The group to sacrifice next: lowest priority among those actually holding a texture
    /// slot, since dropping a height-only group frees nothing and only destroys work.</summary>
    private static LandscapeClaimGroup? LowestPriorityHolder(List<LandscapeClaimGroup> live) =>
        live.Where(group => group.Claims.Any(claim => claim.Material is { PaintsTexture: true }))
            .OrderBy(group => group.Priority)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .FirstOrDefault();

    private static bool Kill(
        List<LandscapeClaimGroup> live,
        List<string> dropped,
        IReadOnlyList<LandscapeClaimGroup> victims)
    {
        if (victims.Count == 0)
        {
            return false;
        }

        // Group atomicity: losing one claim loses the whole group, because "both or neither" is what
        // grouping means. That can free a layer another group wanted, so the caller loops.
        foreach (LandscapeClaimGroup victim in victims.Distinct().OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            if (live.Remove(victim))
            {
                dropped.Add(victim.Key);
            }
        }

        return true;
    }

    private readonly record struct Entry(LandscapeClaimGroup Group, LandscapeClaim Claim);

    private static IEnumerable<Entry> Entries(List<LandscapeClaimGroup> live) =>
        live.SelectMany(group => group.Claims.Select(claim => new Entry(group, claim)));

    // Higher priority wins; the key breaks ties so the outcome never depends on scan order.
    private static (int, string) Rank(Entry entry) => (entry.Group.Priority, entry.Group.Key);

    private static LandscapeClaimGroup Without(LandscapeClaimGroup group, IReadOnlyList<LandscapeClaim> claims) =>
        new()
        {
            Key = group.Key,
            Label = group.Label,
            Priority = group.Priority,
            Claims = claims,
            Source = group.Source,
        };

    private static void AddOnce(List<LandscapeProblem> problems, LandscapeProblem problem)
    {
        // Build() runs once per drop iteration, so a standing problem would otherwise pile up.
        if (!problems.Any(existing => existing.Kind == problem.Kind && existing.Message == problem.Message))
        {
            problems.Add(problem);
        }
    }
}
