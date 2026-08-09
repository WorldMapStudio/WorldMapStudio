using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Streams landscape chunks around the viewport focus, building them from the deformers that reach
/// them. Chunks are computed rather than read from a table, so this is an
/// <see cref="ISceneEntityLoader"/> rather than a storage factory — there is nothing to query and no
/// lock to take.
///
/// Deformers come from the <b>scene registry</b>, not from storage, so a stamp being dragged deforms
/// the terrain immediately rather than only after a commit. That is sound for chunks in the streaming
/// box: streaming loads entities whose bounds overlap that box, and any deformer reaching a chunk
/// inside the box necessarily overlaps the box too. It is <em>not</em> sound for halo chunks outside
/// it, which matters only once a function declares a non-zero sample radius. Phase 7's dirty tracking
/// is where this becomes a real query.
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
        if (_landscape.TakeSnapshot() is not { } snapshot)
        {
            return Task.FromResult<IReadOnlyList<SceneEntity>>([]);
        }

        var builder = new LandscapeBuilder(snapshot.Settings, snapshot.Catalog, snapshot.Functions);
        List<ChunkCoord> coords = builder.Grid.Overlapping(region).ToList();
        if (coords.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<SceneEntity>>([]);
        }

        LandscapeBuildResult result = builder.Build(coords, snapshot.Deformers);
        _landscape.ReportProblems(result.Problems);

        List<SceneEntity> chunks = coords
            .Where(coord => result.Chunks.ContainsKey(coord))
            .Select(coord => (SceneEntity)new LandscapeChunk(result.Chunks[coord], builder.Grid, map))
            .ToList();

        return Task.FromResult<IReadOnlyList<SceneEntity>>(chunks);
    }
}
