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
            string key = deformer.DeformerKey;
            Aabb bounds = deformer.InfluenceBounds;
            int version = deformer.ContentVersion;
            seen.Add(key);

            if (!_known.TryGetValue(key, out (Aabb Bounds, int Version) previous))
            {
                regions.Add(bounds);
            }
            else if (previous.Version != version || previous.Bounds != bounds)
            {
                regions.Add(previous.Bounds);
                regions.Add(bounds);
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
