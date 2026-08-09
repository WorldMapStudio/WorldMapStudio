using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Streams landscape chunks around the viewport focus. Chunks are generated from the grid rather than
/// read from a table, so this is an <see cref="ISceneEntityLoader"/> rather than a storage factory —
/// there is nothing to query and no lock to take.
///
/// Phase 5 hands back flat placeholder output; the builder replaces <see cref="Build"/> in Phase 6
/// without the streaming side changing.
/// </summary>
public sealed class LandscapeChunkLoader : ISceneEntityLoader
{
    private readonly LandscapeSystem _landscape;

    public LandscapeChunkLoader(LandscapeSystem landscape)
    {
        _landscape = landscape;
    }

    public bool Handles(SceneEntity entity) => entity is LandscapeChunk;

    public long KeyOf(SceneEntity entity)
    {
        var chunk = (LandscapeChunk)entity;
        return chunk.Coord.KeyFor(chunk.Map);
    }

    public Task<IReadOnlyList<SceneEntity>> ScanAsync(MapId map, Aabb region)
    {
        // Read once: this runs on a scan continuation, off the main thread, while the user may be
        // editing. Both are reference reads of immutable-enough state; the builder in Phase 6 takes
        // a proper snapshot when it moves onto the work queue.
        LandscapeSettings? settings = _landscape.Settings;
        LandscapeTextureMaterial? fallback = _landscape.FallbackMaterial;

        if (settings == null)
        {
            return Task.FromResult<IReadOnlyList<SceneEntity>>([]);
        }

        // Generating chunks is cheap and synchronous today. It becomes real work in Phase 6, which is
        // when it moves onto the work queue rather than blocking the scan.
        var grid = new LandscapeGrid(settings);
        List<SceneEntity> chunks = grid.Overlapping(region)
            .Select(coord => (SceneEntity)new LandscapeChunk(
                LandscapeChunkOutput.Flat(coord, settings, fallback), grid, map))
            .ToList();

        return Task.FromResult<IReadOnlyList<SceneEntity>>(chunks);
    }
}
