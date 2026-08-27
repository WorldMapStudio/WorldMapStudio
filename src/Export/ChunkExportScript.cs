using System.Collections.Generic;
using System.Threading.Tasks;

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
}
