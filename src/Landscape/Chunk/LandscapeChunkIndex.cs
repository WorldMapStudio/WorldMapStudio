using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// A coord→chunk map over the loaded <see cref="LandscapeChunk"/>s, so callers that know which chunk
/// a world point falls in can look it up instead of scanning every loaded chunk. Rebuilt lazily the
/// first time it is read after <see cref="SceneEntityRegistry.Version"/> moves, so nothing has to
/// remember to keep it in step with streaming.
/// </summary>
public sealed class LandscapeChunkIndex
{
    private readonly SceneEntityRegistry _scene;
    private readonly Dictionary<ChunkCoord, LandscapeChunk> _byCoord = [];
    private int _indexedVersion = -1;

    public LandscapeChunkIndex(SceneEntityRegistry scene)
    {
        _scene = scene;
    }

    /// <summary>The loaded chunk at <paramref name="coord"/>, or null if none is loaded there.</summary>
    public LandscapeChunk? At(ChunkCoord coord)
    {
        EnsureCurrent();
        return _byCoord.GetValueOrDefault(coord);
    }

    /// <summary>The whole current map, for callers that iterate a candidate set themselves.</summary>
    public IReadOnlyDictionary<ChunkCoord, LandscapeChunk> ByCoord
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
        foreach (LandscapeChunk chunk in _scene.Entities.OfType<LandscapeChunk>())
        {
            _byCoord[chunk.Coord] = chunk;
        }
    }
}
