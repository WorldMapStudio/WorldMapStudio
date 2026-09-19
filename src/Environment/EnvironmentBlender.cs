using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// The pure blend step behind <see cref="EnvironmentSystem"/>, pulled out so it can be tested without
/// a scene: given a set of sources and a position and time, resolves which are active and folds them
/// into one <see cref="EnvironmentValues"/>.
/// </summary>
public static class EnvironmentBlender
{
    public readonly record struct Result(
        EnvironmentValues Current,
        IReadOnlyList<(IEnvironmentSource Source, float Weight)> Active);

    /// <summary>
    /// Blends every source in range at <paramref name="focus"/>. The global source is the base,
    /// always included at weight 1 — the one with the highest
    /// <see cref="IEnvironmentSource.GlobalPriority"/> when a map has more than one, ties going to
    /// first-found. The rest are weighted by <see cref="IEnvironmentSource.WeightAt"/> and folded in
    /// by ascending <see cref="IEnvironmentSource.BlendLayer"/>, strongest to weakest within a layer.
    /// Composing strongest-first means a source near full weight (deep in its own radius) all but
    /// overwrites the base outright, and every weaker source after it only nudges the result — the
    /// opposite order would let a distant, barely-in-range source's small blend still count for as
    /// much as it would applied anywhere else in the sequence. This does not by itself produce a
    /// smooth transition between two similar-strength overlapping sources: right where their weights
    /// cross, which one is treated as "strongest" flips, and the composite is not symmetric in the
    /// two sources at that point — a real, period-accurate discontinuity, not a bug to design away.
    /// </summary>
    public static Result Blend(IEnumerable<IEnvironmentSource> sources, Vector3 focus, EnvironmentTime time)
    {
        IEnvironmentSource? global = null;
        var weighted = new List<(IEnvironmentSource Source, float Weight)>();

        foreach (IEnvironmentSource source in sources)
        {
            if (source.IsGlobal)
            {
                if (global is null || source.GlobalPriority > global.GlobalPriority)
                {
                    global = source;
                }

                continue;
            }

            float weight = source.WeightAt(focus);
            if (weight > 0.0f)
            {
                weighted.Add((source, weight));
            }
        }

        weighted.Sort((a, b) =>
        {
            int byLayer = a.Source.BlendLayer.CompareTo(b.Source.BlendLayer);
            return byLayer != 0 ? byLayer : b.Weight.CompareTo(a.Weight);
        });

        EnvironmentValues blended = global?.Evaluate(time) ?? new EnvironmentValues();
        foreach ((IEnvironmentSource source, float weight) in weighted)
        {
            blended = EnvironmentValues.Blend(blended, source.Evaluate(time), weight);
        }

        var active = new List<(IEnvironmentSource, float)>(weighted.Count + 1);
        if (global != null)
        {
            active.Add((global, 1.0f));
        }

        active.AddRange(weighted);

        return new Result(blended, active);
    }
}
