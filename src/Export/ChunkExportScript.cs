using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
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

    /// <summary>Captures this exporter's current settings fields into a profile-storable blob.</summary>
    JsonObject SaveSettings();

    /// <summary>Restores settings fields from a profile-storable blob produced by <see cref="SaveSettings"/>.
    /// Missing keys (a freshly-created profile, or a settings shape from an older version) must fall
    /// back to sensible defaults rather than throwing.</summary>
    void LoadSettings(JsonObject settings);

    Task<ChunkExportResult> ExportAsync(
        ChunkExportContext context,
        IReadOnlyList<ChunkChange> chunks,
        WorkContext work);
}

public sealed class ChunkExportContext
{
    private readonly ExportSystem _exports;
    private readonly string _profileId;

    internal ChunkExportContext(ExportSystem exports, string profileId)
    {
        _exports = exports;
        _profileId = profileId;
    }

    public string ProjectFolder => ProjectStore.ProjectFolder(_exports.Context.Project.Name);

    /// <summary>Builds any number of chunks with one scene scan, rather than one per chunk — what a
    /// tile-shaped exporter needs.</summary>
    public Task<LandscapeBuildResult?> BuildLandscapeChunksAsync(MapId map, IReadOnlyList<ChunkCoord> coords) =>
        _exports.BuildLandscapeChunksAsync(map, coords);

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

    /// <summary>The editor's general problem list — where an exporter reports things it found wrong
    /// with the data it was asked to export (an unusable texture path, settings that don't match its
    /// target format), the same list the landscape builder itself reports into.</summary>
    public ProblemSystem Problems => _exports.Context.Problems;

    /// <summary>The whole editor context — an escape hatch for a plugin exporter that needs more than
    /// the narrow slices above (e.g. a background scan already written expecting to take
    /// <c>EditorContext</c> directly, the way <c>WowLiquidTileBackgroundScan</c> was).</summary>
    public EditorContext EditorContext => _exports.Context;

    /// <summary>This export profile's previously-assigned stable entity ids — for a target format that
    /// needs one but has no id of its own to reuse.</summary>
    public Task<IReadOnlyDictionary<long, long>> LoadExportedEntityIdsAsync() =>
        _exports.LoadExportedEntityIdsAsync(_profileId);

    /// <summary>Persists newly-assigned or changed entity ids from <see cref="LoadExportedEntityIdsAsync"/>.</summary>
    public Task SaveExportedEntityIdsAsync(IReadOnlyDictionary<long, long> ids) =>
        _exports.UpsertExportedEntityIdsAsync(_profileId, ids);
}
