using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>A block's built chunks, plus everything that went wrong building them.</summary>
public sealed class LandscapeBuildResult
{
    public required IReadOnlyDictionary<ChunkCoord, LandscapeChunkOutput> Chunks { get; init; }

    /// <summary>Problems keyed by the chunk that produced them.</summary>
    public required IReadOnlyList<(ChunkCoord Coord, LandscapeProblem Problem)> Problems { get; init; }
}

/// <summary>
/// Builds a block of chunks through the five stages, run one at a time over the whole block rather
/// than per chunk:
///
/// <list type="number">
/// <item>gather the deformers touching the block and its halo,</item>
/// <item>claim and resolve — <b>over the halo too</b>, because what an entity writes into a halo
/// chunk's channels depends on how its claims resolved <em>there</em>,</item>
/// <item>rasterize channels for block + halo, write-only,</item>
/// <item>evaluate outputs for the interior, which may now sample any channel in the neighbourhood,</item>
/// <item>emit.</item>
/// </list>
///
/// The invariant the whole thing hangs on: <b>rasterization never reads channels and evaluation never
/// writes them</b>. That is what makes stage 3 order-independent and lets stage 4 sample across chunk
/// borders knowing nothing is half-built.
///
/// Runs off the main thread, and touches no Godot node and no database while it does — but it is not
/// yet pure. The deformers it is handed are the live scene entities, not copies, so an inspector edit
/// landing mid-build can be read half-applied; the chunk that results is corrected by the rebuild that
/// same edit triggers. Snapshotting deformers at capture time (where <see cref="LandscapeSystem.TakeSnapshot"/>
/// already clones the settings) is what would close that, and would retire
/// <see cref="ILandscapeDeformer.ContentVersion"/>'s hash fingerprint with it.
/// </summary>
public sealed class LandscapeBuilder
{
    private readonly LandscapeSettings _settings;
    private readonly LandscapeCatalog _catalog;
    private readonly LandscapeFunctions _functions;
    private readonly LandscapeChannelPool _pool;

    public LandscapeBuilder(LandscapeSettings settings, LandscapeCatalog catalog, LandscapeFunctions functions)
    {
        _settings = settings;
        _catalog = catalog;
        _functions = functions;
        _pool = new LandscapeChannelPool(settings, catalog.Channels);
        Grid = new LandscapeGrid(settings);
    }

    public LandscapeGrid Grid { get; }

    /// <summary>How far outside a chunk any bound function samples. Sizes the halo and the dirty set.</summary>
    public float SampleRadius => _catalog.MaxSampleRadius;

    public LandscapeBuildResult Build(IReadOnlyList<ChunkCoord> interior, IReadOnlyList<ILandscapeDeformer> deformers)
    {
        _pool.Reset();

        // Stage 1–2 cover the halo as well: an interior chunk samples its neighbours' channels, and
        // what went into those depends on how claims resolved over there.
        List<ChunkCoord> neighbourhood = Neighbourhood(interior);
        var resolutions = new Dictionary<ChunkCoord, LandscapeResolution>();
        var problems = new List<(ChunkCoord, LandscapeProblem)>();

        foreach (ChunkCoord coord in neighbourhood)
        {
            LandscapeResolution resolution = ResolveChunk(coord, deformers);
            resolutions[coord] = resolution;

            if (interior.Contains(coord))
            {
                problems.AddRange(resolution.Problems.Select(problem => (coord, problem)));
            }
        }

        // Stage 3: pure scatter over the neighbourhood. No ordering constraints between chunks.
        foreach (ChunkCoord coord in neighbourhood)
        {
            var context = new LandscapeRasterContext(coord, _pool, resolutions[coord]);
            foreach (ILandscapeDeformer deformer in Touching(deformers, coord))
            {
                deformer.Rasterize(context);
            }
        }

        // Stage 4–5: only now does anything read, and every channel it can reach is final.
        var chunks = new Dictionary<ChunkCoord, LandscapeChunkOutput>();
        foreach (ChunkCoord coord in interior)
        {
            chunks[coord] = Evaluate(coord, resolutions[coord], problems);
        }

        return new LandscapeBuildResult { Chunks = chunks, Problems = problems };
    }

    /// <summary>Convenience for building a single chunk with its own halo.</summary>
    public LandscapeChunkOutput BuildOne(ChunkCoord coord, IReadOnlyList<ILandscapeDeformer> deformers) =>
        Build([coord], deformers).Chunks[coord];

    private List<ChunkCoord> Neighbourhood(IReadOnlyList<ChunkCoord> interior)
    {
        int halo = SampleRadius <= 0.0f ? 0 : Mathf.CeilToInt(SampleRadius / Grid.ChunkSize);
        if (halo == 0)
        {
            return interior.ToList();
        }

        var coords = new HashSet<ChunkCoord>();
        foreach (ChunkCoord coord in interior)
        {
            for (int y = -halo; y <= halo; y++)
            {
                for (int x = -halo; x <= halo; x++)
                {
                    var neighbour = new ChunkCoord(coord.X + x, coord.Y + y);
                    if (Grid.IsInLimits(neighbour))
                    {
                        coords.Add(neighbour);
                    }
                }
            }
        }

        // Stable order so a rebuild walks the neighbourhood identically.
        return coords.OrderBy(coord => coord.Y).ThenBy(coord => coord.X).ToList();
    }

    private LandscapeResolution ResolveChunk(ChunkCoord coord, IReadOnlyList<ILandscapeDeformer> deformers)
    {
        var context = new LandscapeClaimContext(coord, Grid, _catalog);
        List<LandscapeClaimGroup> groups = Touching(deformers, coord)
            .SelectMany(deformer => deformer.Claim(context))
            .ToList();

        return LandscapeResolver.Resolve(groups, _settings, _catalog);
    }

    private IEnumerable<ILandscapeDeformer> Touching(IReadOnlyList<ILandscapeDeformer> deformers, ChunkCoord coord)
    {
        Aabb bounds = Grid.BoundsOf(coord);
        return deformers
            .Where(deformer => deformer.InfluenceBounds.Intersects(bounds))
            .OrderBy(deformer => deformer.DeformerKey, System.StringComparer.Ordinal);
    }

    private LandscapeChunkOutput Evaluate(
        ChunkCoord coord,
        LandscapeResolution resolution,
        List<(ChunkCoord, LandscapeProblem)> problems)
    {
        int heightResolution = Mathf.Max(2, _settings.ChunkHeightResolution);
        int alphaResolution = Mathf.Max(1, _settings.ChunkAlphaResolution);

        var heights = new float[heightResolution * heightResolution];
        foreach (LandscapeClaim claim in resolution.HeightClaims)
        {
            // A height claim that deforms nothing is otherwise completely silent: it resolves fine,
            // takes no slot, and simply does not run. That is a confusing thing to debug by looking
            // at flat terrain, so say so.
            if (claim.Material is not { } material)
            {
                problems.Add((coord, LandscapeProblem.Create(
                    LandscapeProblemKind.MissingMaterial,
                    $"Height layer '{claim.Layer.Name}' was claimed without a material, so nothing deforms.")));
                continue;
            }

            if (_functions.FindHeight(material.HeightFunction) is not { } function)
            {
                string reason = material.HeightFunction.Length == 0
                    ? "binds no height function"
                    : $"binds height function '{material.HeightFunction}', which nothing provides";
                problems.Add((coord, LandscapeProblem.Create(
                    LandscapeProblemKind.MissingHeightFunction,
                    $"Height layer '{claim.Layer.Name}' uses material '{material.Name}', which {reason}, so nothing deforms.")));
                continue;
            }

            // Height claims arrive in draw order, and each rewrites what the previous ones built —
            // which is exactly why flatten can cut into a raise rather than only adding to it.
            var values = LandscapeParameterValues.Parse(material.HeightParameters);
            var context = new LandscapeEvalContext(coord, _pool, _settings, values, heightResolution);
            function.Evaluate(context, heights);
        }

        var layers = new List<LandscapeChunkLayer>();
        foreach (LandscapeSlot slot in resolution.Slots)
        {
            if (!slot.IsBase && !slot.Material.PaintsTexture)
            {
                problems.Add((coord, LandscapeProblem.Create(
                    LandscapeProblemKind.MissingAlphaFunction,
                    $"Layer '{slot.Layers[0].Name}' uses material '{slot.Material.Name}', which has no alpha " +
                    "function, so nothing decides where it shows.")));
            }

            layers.Add(new LandscapeChunkLayer
            {
                Material = slot.Material,
                Alpha = slot.IsBase ? null : EvaluateAlpha(coord, slot, alphaResolution),
            });
        }

        return new LandscapeChunkOutput
        {
            Coord = coord,
            HeightResolution = heightResolution,
            Heights = heights,
            AlphaResolution = alphaResolution,
            Layers = layers,
        };
    }

    private byte[] EvaluateAlpha(ChunkCoord coord, LandscapeSlot slot, int resolution)
    {
        var coverage = new float[resolution * resolution];

        // A merged slot needs one evaluation, not one per layer: merging requires an identical
        // material, so every layer in it resolves to the same function, parameters and channel, and
        // would produce the same coverage.
        if (_functions.FindAlpha(slot.Material.AlphaFunction) is { } function)
        {
            var values = LandscapeParameterValues.Parse(slot.Material.AlphaParameters);
            var context = new LandscapeEvalContext(coord, _pool, _settings, values, resolution);
            function.Evaluate(context, coverage);
        }

        var alpha = new byte[coverage.Length];
        for (int i = 0; i < coverage.Length; i++)
        {
            alpha[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(coverage[i] * 255.0f), 0, 255);
        }

        return alpha;
    }
}
