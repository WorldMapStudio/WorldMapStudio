using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>What a deformer needs to decide which layers it wants in a chunk.</summary>
public readonly struct LandscapeClaimContext
{
    public LandscapeClaimContext(ChunkCoord coord, LandscapeGrid grid, LandscapeCatalog catalog)
    {
        Coord = coord;
        Grid = grid;
        Catalog = catalog;
    }

    public ChunkCoord Coord { get; }

    public LandscapeGrid Grid { get; }

    /// <summary>Where an entity resolves the layer and material ids its instance parameters hold.</summary>
    public LandscapeCatalog Catalog { get; }

    public LandscapeLayer? Layer(int? recordId) =>
        recordId is { } id ? Find(Catalog.Layers, layer => layer.RecordId == id) : null;

    public LandscapeMaterial? Material(int? recordId) =>
        recordId is { } id ? Find(Catalog.Materials, material => material.RecordId == id) : null;

    private static T? Find<T>(IReadOnlyList<T> items, System.Func<T, bool> match) where T : class
    {
        foreach (T item in items)
        {
            if (match(item))
            {
                return item;
            }
        }

        return null;
    }
}

/// <summary>
/// What a deformer needs to write its channels — including how its claims resolved, so it can react
/// to having won, been merged into another slot, or been dropped entirely.
///
/// Writes go only to this chunk. Nothing here reads a channel: rasterization is a pure scatter, which
/// is what lets the whole neighbourhood be rasterized in any order before any function samples it.
/// </summary>
public readonly struct LandscapeRasterContext
{
    private readonly LandscapeChannelPool _pool;

    public LandscapeRasterContext(ChunkCoord coord, LandscapeChannelPool pool, LandscapeResolution resolution)
    {
        Coord = coord;
        _pool = pool;
        Resolution = resolution;
    }

    public ChunkCoord Coord { get; }

    public LandscapeGrid Grid => _pool.Grid;

    /// <summary>How this chunk's claims resolved.</summary>
    public LandscapeResolution Resolution { get; }

    /// <summary>The channel's texel grid for this chunk, or null when no such channel exists.</summary>
    public float[]? Buffer(string channelName) => _pool.Buffer(channelName, Coord);

    public LandscapeChannel? Channel(string channelName) => _pool.Channel(channelName);

    /// <summary>World position of a channel texel's centre in this chunk.</summary>
    public Vector3 TexelCentre(int resolution, int x, int y) => _pool.TexelCentre(Coord, resolution, x, y);
}

/// <summary>
/// A scene entity that shapes the landscape. Implemented alongside <see cref="SceneEntity"/>, so a
/// deformer is an ordinary entity that streams, selects and undoes like any other — the landscape is
/// a function of these, and moving one moves the terrain.
///
/// The two calls are deliberately separate. Claiming is cheap and side-effect free so the resolver
/// can decide the whole chunk before anything is drawn; rasterizing is told the outcome.
/// </summary>
public interface ILandscapeDeformer
{
    /// <summary>
    /// Stable identity for this deformer's claim groups. Must not depend on scan order or runtime
    /// object identity — the resolver breaks ties on it, and terrain that resolves differently per
    /// machine is not reproducible. Build it from a persistent key.
    /// </summary>
    string DeformerKey { get; }

    /// <summary>World bounds this deformer influences, used to find the chunks it touches.</summary>
    Aabb InfluenceBounds { get; }

    /// <summary>
    /// Changes whenever anything that alters this deformer's output changes — its shape, its
    /// bindings, its parameters. Incremental rebuilding watches this to know which chunks went stale,
    /// so a field left out of it is a field whose edits do not show up until something else forces a
    /// rebuild.
    ///
    /// Position and size need not be included: <see cref="InfluenceBounds"/> is compared separately.
    ///
    /// A fingerprint, not a counter — implementations hash their fields, which means two different
    /// states can collide and read as unchanged. Acceptable only because it is compared against a
    /// value from the same process and never persisted: <c>HashCode</c> is seeded randomly per run, so
    /// a stored one is meaningless on the next launch. Do not write this to the database or send it
    /// over the HTTP endpoint.
    /// </summary>
    int ContentVersion { get; }

    /// <summary>What this deformer wants in the chunk. Pure: no rasterization, no channel writes.</summary>
    IEnumerable<LandscapeClaimGroup> Claim(in LandscapeClaimContext context);

    /// <summary>Writes this deformer's contribution into the chunk's channels.</summary>
    void Rasterize(in LandscapeRasterContext context);
}
