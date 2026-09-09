using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// A coord→chunk map over the loaded <see cref="LandscapeTerrainBatch"/>es, so callers that know
/// which chunk a world point falls in can look up its built output (and the batch drawing it) instead
/// of scanning every loaded batch. Rebuilt lazily the first time it is read after
/// <see cref="SceneEntityRegistry.Version"/> moves, so nothing has to keep it in step with streaming.
/// </summary>
public sealed class LandscapeChunkIndex
{
    private readonly SceneEntityRegistry _scene;
    private readonly Dictionary<ChunkCoord, (LandscapeTerrainBatch Batch, LandscapeChunkOutput Output)> _byCoord = [];
    private int _indexedVersion = -1;

    public LandscapeChunkIndex(SceneEntityRegistry scene)
    {
        _scene = scene;
    }

    /// <summary>The built output at <paramref name="coord"/>, or null if no loaded batch covers it.</summary>
    public LandscapeChunkOutput? OutputAt(ChunkCoord coord)
    {
        EnsureCurrent();
        return _byCoord.TryGetValue(coord, out (LandscapeTerrainBatch Batch, LandscapeChunkOutput Output) entry)
            ? entry.Output
            : null;
    }

    /// <summary>The batch drawing the chunk at <paramref name="coord"/>, or null.</summary>
    public LandscapeTerrainBatch? BatchAt(ChunkCoord coord)
    {
        EnsureCurrent();
        return _byCoord.TryGetValue(coord, out (LandscapeTerrainBatch Batch, LandscapeChunkOutput Output) entry)
            ? entry.Batch
            : null;
    }

    /// <summary>The whole current map, for callers that iterate a candidate set themselves.</summary>
    public IReadOnlyDictionary<ChunkCoord, (LandscapeTerrainBatch Batch, LandscapeChunkOutput Output)> ByCoord
    {
        get
        {
            EnsureCurrent();
            return _byCoord;
        }
    }

    private void EnsureCurrent()
    {
        if (_indexedVersion == _scene.Version)
        {
            return;
        }

        _indexedVersion = _scene.Version;
        _byCoord.Clear();
        foreach (LandscapeTerrainBatch batch in _scene.Entities.OfType<LandscapeTerrainBatch>())
        {
            foreach (KeyValuePair<ChunkCoord, LandscapeChunkOutput> pair in batch.Chunks)
            {
                _byCoord[pair.Key] = (batch, pair.Value);
            }
        }
    }
}
