using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Remembers where each deformer was and what it looked like, so a change can be turned into the
/// world regions that went stale rather than rebuilding everything.
///
/// A moved deformer dirties <b>two</b> regions: where it was, which must go back to what it would be
/// without it, and where it is now. Forgetting the first is the classic incremental-rebuild bug — the
/// terrain keeps a ghost of the shape wherever the entity used to be.
/// </summary>
public sealed class LandscapeDirtyTracker
{
    private readonly Dictionary<string, (Aabb Bounds, int Version)> _known = [];

    /// <summary>How many deformers are being tracked, for the debug window.</summary>
    public int Count => _known.Count;

    /// <summary>
    /// Records the current state and returns the regions that changed since the last call. On the
    /// first call every deformer is new, which is correct: nothing has been built from them yet.
    /// </summary>
    public IReadOnlyList<Aabb> Collect(IReadOnlyList<ILandscapeDeformer> deformers)
    {
        var regions = new List<Aabb>();
        var seen = new HashSet<string>();

        foreach (ILandscapeDeformer deformer in deformers)
        {
            Aabb bounds = deformer.InfluenceBounds;
            if (bounds.Size == Vector3.Zero)
            {
                // A degenerate influence box (e.g. a procedural mesh with nothing to paint) is
                // skipped rather than tracked. If it was tracked with real bounds before (a paint
                // model edited down to nothing), leaving its key out of `seen` below makes the "gone"
                // sweep dirty its old region, which is exactly the invalidation that edit needs.
                continue;
            }

            string key = deformer.DeformerKey;
            int version = deformer.ContentVersion;
            seen.Add(key);

            if (!_known.TryGetValue(key, out (Aabb Bounds, int Version) previous))
            {
                regions.Add(bounds);
                (deformer as IIncrementalLandscapeDeformer)?.ConsumeDirtyRegions();
            }
            else if (previous.Bounds != bounds)
            {
                regions.Add(previous.Bounds);
                regions.Add(bounds);
                (deformer as IIncrementalLandscapeDeformer)?.ConsumeDirtyRegions();
            }
            else if (previous.Version != version)
            {
                // A moved-nowhere content edit: most deformers (a stamp's falloff, a material bind)
                // recompute their whole footprint from parameters anyway, so the whole bounds is the
                // right answer. One that opts in (ImageComponent, over a possibly huge painted canvas)
                // instead reports just the sub-region a single brush dab actually touched — the
                // difference between one paint stroke rebuilding a handful of chunks and rebuilding
                // every chunk under the entire image on every frame it drags.
                if (deformer is IIncrementalLandscapeDeformer incremental)
                {
                    regions.AddRange(incremental.ConsumeDirtyRegions());
                }
                else
                {
                    regions.Add(bounds);
                }
            }

            _known[key] = (bounds, version);
        }

        foreach (string gone in _known.Keys.Where(key => !seen.Contains(key)).ToList())
        {
            regions.Add(_known[gone].Bounds);
            _known.Remove(gone);
        }

        return regions;
    }


    public void Clear() => _known.Clear();
}
