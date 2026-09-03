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

    /// <summary>
    /// One chunk beyond the view, plus whatever the bound functions reach.
    ///
    /// A chunk only partly inside the view is still built whole, so something overlapping its far
    /// half can sit a full chunk outside — and halo chunks, whose channels the visible ones sample,
    /// need their own deformers too. Under-declaring this is not a crash: edge chunks simply come out
    /// different depending on what happened to be loaded, and settle only once you fly closer.
    /// </summary>
    public float LoadMargin
    {
        get
        {
            if (_landscape.Settings is not { } settings)
            {
                return 0.0f;
            }

            return settings.ChunkWorldSize + _landscape.Catalog.MaxSampleRadius;
        }
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
    public void Prepare()
    {
        _snapshot = _landscape.TakeSnapshot();

        // Also captured here rather than in ScanAsync: which coords are already loaded decides which
        // ones this scan can skip rebuilding (see ScanAsync), and the scene registry is exactly the
        // live editor state Prepare exists to snapshot before the scan can hop off the main thread.
        _loadedCoords.Clear();
        foreach (LandscapeChunk chunk in _landscape.Context.Scene.Entities.OfType<LandscapeChunk>())
        {
            _loadedCoords.Add((chunk.Map, chunk.Coord));
        }
    }

    private LandscapeSystem.BuildSnapshot? _snapshot;
    private readonly HashSet<(MapId Map, ChunkCoord Coord)> _loadedCoords = [];

    public bool TryRefresh(SceneEntity loaded, SceneEntity rescanned)
    {
        // The rebuilt output is the new truth; the loaded chunk keeps its identity and selection. Its
        // mesh and material were already built off the main thread while the scan was still running,
        // so this — running on the main thread inside StreamingSystem.Reconcile — only swaps a node's
        // children rather than building a mesh and a material for every chunk that just streamed in.
        var rescannedChunk = (LandscapeChunk)rescanned;
        ((LandscapeChunk)loaded).Rebuild(rescannedChunk.Output, rescannedChunk.Mesh, rescannedChunk.Material);
        return true;
    }

    public async Task<IReadOnlyList<SceneEntity>> ScanAsync(MapId map, Aabb region)
    {
        if (_snapshot is not { } snapshot)
        {
            return [];
        }

        var builder = new LandscapeBuilder(snapshot.Settings, snapshot.Catalog, snapshot.Functions);

        // A chunk already loaded at this coordinate needs no rebuilding here: keeping loaded chunks in
        // step with whatever shapes them is LandscapeRebuilder's job, running independently every
        // frame regardless of streaming's own scan cadence. Without this filter, every rescan rebuilt
        // — full height/alpha evaluation, mesh upload, material construction — the *entire* visible
        // chunk set from scratch, not just whatever newly entered view; at a real view distance that
        // is hundreds of chunks redone for nothing on every ~32 units of camera travel.
        List<ChunkCoord> coords = builder.Grid.Overlapping(region)
            .Where(coord => !_loadedCoords.Contains((map, coord)))
            .ToList();
        if (coords.Count == 0)
        {
            return [];
        }

        // Explicitly off the main thread. A scan continuation only *usually* resumes on a worker —
        // an uncontended reader lock can complete synchronously and leave the whole build on the
        // thread that started it, which is a visible stall every time the camera moves far enough.
        LandscapeBuildResult result = await Task.Run(() => builder.Build(coords, snapshot.Deformers))
            .ConfigureAwait(false);

        _landscape.Reporter.Report(result, builder.Grid, snapshot.Deformers);

        // Still off the main thread here (ConfigureAwait(false) throughout keeps the continuation on
        // the worker), so building each chunk's mesh and material — real Godot resource construction —
        // happens where the heightmap build already ran, instead of stalling the frame that streams it in.
        AssetSystem assets = _landscape.Context.Assets;
        return coords
            .Where(coord => result.Chunks.ContainsKey(coord))
            .Select(coord =>
            {
                LandscapeChunkOutput output = result.Chunks[coord];
                ArrayMesh mesh = LandscapeChunkMesh.BuildMesh(output, builder.Grid.ChunkSize);
                ShaderMaterial material = LandscapeChunkMesh.BuildMaterial(output, assets, snapshot.Settings);
                return (SceneEntity)new LandscapeChunk(output, mesh, material, builder.Grid, map);
            })
            .ToList();
    }
}
