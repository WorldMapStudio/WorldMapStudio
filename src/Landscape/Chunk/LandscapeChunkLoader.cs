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

    /// <summary>
    /// Captures the build inputs on the main thread. The catalog memoizes into fields and streaming
    /// mutates the scene registry, so reading either from the scan thread is a race — and one that
    /// fails the whole scan, taking every other entity type down with it.
    /// </summary>
    public void Prepare() => _snapshot = _landscape.TakeSnapshot();

    private LandscapeSystem.BuildSnapshot? _snapshot;

    public bool TryRefresh(SceneEntity loaded, SceneEntity rescanned)
    {
        // The rebuilt output is the new truth; the loaded chunk keeps its identity and selection.
        ((LandscapeChunk)loaded).Rebuild(((LandscapeChunk)rescanned).Output);
        return true;
    }

    public async Task<IReadOnlyList<SceneEntity>> ScanAsync(MapId map, Aabb region)
    {
        if (_snapshot is not { } snapshot)
        {
            return [];
        }

        var builder = new LandscapeBuilder(snapshot.Settings, snapshot.Catalog, snapshot.Functions);
        List<ChunkCoord> coords = builder.Grid.Overlapping(region).ToList();
        if (coords.Count == 0)
        {
            return [];
        }

        // Explicitly off the main thread. A scan continuation only *usually* resumes on a worker —
        // an uncontended reader lock can complete synchronously and leave the whole build on the
        // thread that started it, which is a visible stall every time the camera moves far enough.
        LandscapeBuildResult result = await Task.Run(() => builder.Build(coords, snapshot.Deformers))
            .ConfigureAwait(false);

        _landscape.ReportProblems(result.Problems);

        return coords
            .Where(coord => result.Chunks.ContainsKey(coord))
            .Select(coord => (SceneEntity)new LandscapeChunk(result.Chunks[coord], builder.Grid, map))
            .ToList();
    }
}
