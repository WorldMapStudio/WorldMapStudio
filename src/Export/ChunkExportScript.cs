using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

public enum ChunkExportScope
{
    CurrentMap,
    AllMaps,
}

public readonly record struct ChunkChange(MapId Map, ChunkCoord Coord, string ContentHash);

public readonly record struct ChunkExportResult(int ExportedChunks, string Message);

public interface IChunkExportScript : ISubsystem
{
    string Id { get; }

    string DisplayName { get; }

    void DrawSettings();

    Task<ChunkExportResult> ExportAsync(
        ChunkExportContext context,
        IReadOnlyList<ChunkChange> chunks,
        WorkContext work);
}

public sealed class ChunkExportContext
{
    private readonly ExportSystem _exports;
    private readonly LandscapeCatalog _catalog;
    private readonly LandscapeFunctions _functions;

    internal ChunkExportContext(ExportSystem exports, LandscapeCatalog catalog, LandscapeFunctions functions)
    {
        _exports = exports;
        _catalog = catalog;
        _functions = functions;
    }

    public string ProjectFolder => ProjectStore.ProjectFolder(_exports.Context.Project.Name);

    public Task<LandscapeChunkOutput?> BuildLandscapeChunkAsync(MapId map, ChunkCoord coord) =>
        _exports.BuildLandscapeChunkAsync(map, coord, _catalog, _functions);

    /// <summary>Builds any number of chunks with one scene scan, rather than one per chunk — the
    /// batch counterpart of <see cref="BuildLandscapeChunkAsync"/> a tile-shaped exporter needs.</summary>
    public Task<LandscapeBuildResult?> BuildLandscapeChunksAsync(MapId map, IReadOnlyList<ChunkCoord> coords) =>
        _exports.BuildLandscapeChunksAsync(map, coords, _catalog, _functions);

    /// <summary>Scene entities overlapping <paramref name="region"/> — placements, procedural models,
    /// anything an exporter needs beyond the landscape itself.</summary>
    public Task<IReadOnlyList<SceneEntity>> ScanSceneAsync(MapId map, Aabb region) =>
        _exports.ScanSceneAsync(map, region);

    /// <summary>The map an export scope's <see cref="ChunkChange.Map"/> refers to, or null if it no
    /// longer exists.</summary>
    public Map? FindMap(MapId map) => _exports.Context.Maps.Maps.FirstOrDefault(candidate => candidate.Id == map);

    /// <summary>A map's landscape settings — the chunk sizing/resolution a tile-shaped exporter needs
    /// to build a <see cref="LandscapeGrid"/> of its own, e.g. for chunk-to-world-position math.</summary>
    public LandscapeSettings? LoadLandscapeSettings(MapId map) => _exports.LoadLandscapeSettings(map);
}
