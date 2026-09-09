using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Streams terrain around the viewport focus as <see cref="LandscapeTerrainBatch"/>es — a square of
/// chunks sharing one mesh and material. Chunks are computed rather than read from a table, so this is
/// an <see cref="ISceneEntityLoader"/> rather than a storage factory.
///
/// A batch is built whole or not at all: every in-limits chunk of a batch the view touches is built
/// in one call, so the batch's single mesh never has holes that only fill in as the camera moves.
/// </summary>
public sealed class LandscapeBatchLoader : ISceneEntityLoader
{
    private readonly LandscapeSystem _landscape;

    public LandscapeBatchLoader(LandscapeSystem landscape)
    {
        _landscape = landscape;
    }

    /// <summary>
    /// One chunk beyond the view, plus whatever the bound functions reach. Expanding a partly-visible
    /// batch to whole can pull chunks a little further past the view than that, and those get built
    /// from whatever deformers happen to be loaded — the same "edge terrain settles as you approach"
    /// tolerance the per-chunk loader had, just at a batch boundary. Not widened by the batch size:
    /// that would grow the region <em>every</em> stored entity type is read over, which is its own
    /// load cost far bigger than the occasional wrong batch edge.
    /// </summary>
    public float LoadMargin =>
        _landscape.Settings is { } settings
            ? settings.ChunkWorldSize + _landscape.Catalog.MaxSampleRadius
            : 0.0f;

    public bool Handles(SceneEntity entity) => entity is LandscapeTerrainBatch;

    public long KeyOf(SceneEntity entity)
    {
        var batch = (LandscapeTerrainBatch)entity;
        return batch.BatchCoord.KeyFor(batch.Map);
    }

    /// <summary>
    /// Captures the build inputs on the main thread. The catalog memoizes into fields, streaming
    /// mutates the scene registry, and the batch size is a live view setting — reading any of them
    /// from the scan thread is a race that fails the whole scan.
    /// </summary>
    public void Prepare()
    {
        _snapshot = _landscape.TakeSnapshot();
        _batchChunks = BatchChunks();

        _loadedBatches.Clear();
        foreach (LandscapeTerrainBatch batch in _landscape.Context.Scene.Entities.OfType<LandscapeTerrainBatch>())
        {
            _loadedBatches.Add((batch.Map, batch.BatchCoord));
        }
    }

    private LandscapeSystem.BuildSnapshot? _snapshot;
    private int _batchChunks = 1;
    private readonly HashSet<(MapId Map, LandscapeBatchCoord Coord)> _loadedBatches = [];

    private int BatchChunks() =>
        Mathf.Clamp(_landscape.Context.View.TerrainBatchChunks, 1, LandscapeTerrainBatch.MaxTerrainBatchChunks);

    public bool TryRefresh(SceneEntity loaded, SceneEntity rescanned)
    {
        var fresh = (LandscapeTerrainBatch)rescanned;
        ((LandscapeTerrainBatch)loaded).Rebuild(fresh.Chunks, fresh.Mesh, fresh.Material);
        return true;
    }

    public async Task<IReadOnlyList<SceneEntity>> ScanAsync(MapId map, Aabb region)
    {
        if (_snapshot is not { } snapshot)
        {
            return [];
        }

        int batchChunks = _batchChunks;
        var builder = new LandscapeBuilder(snapshot.Settings, snapshot.Catalog, snapshot.Functions);

        // Group the overlapped coords into batches, drop batches already loaded (LandscapeRebuilder
        // keeps those in step), then expand each remaining batch to every in-limits chunk it holds so
        // its one mesh is built complete.
        var batches = new Dictionary<LandscapeBatchCoord, List<ChunkCoord>>();
        foreach (ChunkCoord coord in builder.Grid.Overlapping(region))
        {
            LandscapeBatchCoord batchCoord = LandscapeBatchCoord.Of(coord, batchChunks);
            if (_loadedBatches.Contains((map, batchCoord)))
            {
                continue;
            }

            if (!batches.TryGetValue(batchCoord, out List<ChunkCoord>? _))
            {
                batches[batchCoord] = ExpandBatch(builder.Grid, batchCoord, batchChunks);
            }
        }

        if (batches.Count == 0)
        {
            return [];
        }

        List<ChunkCoord> union = batches.Values.SelectMany(coords => coords).ToList();

        // Explicitly off the main thread: a continuation only usually resumes on a worker.
        LandscapeBuildResult result = await Task.Run(() => builder.Build(union, snapshot.Deformers))
            .ConfigureAwait(false);

        _landscape.Reporter.Report(result, builder.Grid, snapshot.Deformers);

        // Still off the main thread — mesh and material construction is real Godot resource work and
        // belongs where the heightmap build already ran, not on the frame that streams it in.
        AssetSystem assets = _landscape.Context.Assets;
        var entities = new List<SceneEntity>();
        foreach ((LandscapeBatchCoord batchCoord, List<ChunkCoord> coords) in batches)
        {
            var outputs = new Dictionary<ChunkCoord, LandscapeChunkOutput>();
            foreach (ChunkCoord coord in coords)
            {
                if (result.Chunks.TryGetValue(coord, out LandscapeChunkOutput? output))
                {
                    outputs[coord] = output;
                }
            }

            if (outputs.Count == 0)
            {
                continue;
            }

            var ordered = outputs
                .OrderBy(pair => pair.Key.Y)
                .ThenBy(pair => pair.Key.X)
                .Select(pair => (pair.Key, pair.Value))
                .ToList();

            ArrayMesh mesh = LandscapeBatchMesh.BuildMesh(ordered, batchCoord, builder.Grid, batchChunks);
            ShaderMaterial material = LandscapeBatchMesh.BuildMaterial(
                ordered, batchCoord, batchChunks, assets, snapshot.Settings);
            entities.Add(new LandscapeTerrainBatch(batchCoord, batchChunks, outputs, mesh, material, builder.Grid, map));
        }

        return entities;
    }

    private static List<ChunkCoord> ExpandBatch(LandscapeGrid grid, LandscapeBatchCoord batchCoord, int batchChunks)
    {
        ChunkCoord origin = batchCoord.Origin(batchChunks);
        var coords = new List<ChunkCoord>();
        for (int y = 0; y < batchChunks; y++)
        {
            for (int x = 0; x < batchChunks; x++)
            {
                var coord = new ChunkCoord(origin.X + x, origin.Y + y);
                if (grid.IsInLimits(coord))
                {
                    coords.Add(coord);
                }
            }
        }

        return coords;
    }
}
