using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>
/// Keeps each map's landscape settings in the Editor storage's <c>landscape_settings</c> table, one
/// row per map. Written outside the edit session: settings are not an entity and changing them is a
/// map-wide rebuild, not an undoable edit.
/// </summary>
[Subsystem(nameof(EditorStorage))]
public sealed class EditorLandscapeSettingsSource : ILandscapeSettingsSource
{
    private readonly EditorStorage _storage;

    public EditorLandscapeSettingsSource(EditorStorage storage)
    {
        _storage = storage;
    }

    public float Priority => 0.0f;

    public bool CanEdit => true;

    public async Task<LandscapeSettings?> LoadAsync(MapId map)
    {
        await using EditorDbContext context = _storage.CreateContext();
        LandscapeSettingsRecord? record = await context.LandscapeSettings.AsNoTracking()
            .FirstOrDefaultAsync(row => row.MapId == map.Value)
            .ConfigureAwait(false);

        return record == null ? null : ToSettings(record);
    }

    public async Task SaveAsync(MapId map, LandscapeSettings settings)
    {
        await using EditorDbContext context = _storage.CreateContext();
        LandscapeSettingsRecord? existing = await context.LandscapeSettings
            .FirstOrDefaultAsync(row => row.MapId == map.Value)
            .ConfigureAwait(false);

        if (existing == null)
        {
            var created = new LandscapeSettingsRecord { MapId = map.Value };
            WriteRecord(settings, created);
            context.LandscapeSettings.Add(created);
        }
        else
        {
            WriteRecord(settings, existing);
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    private static LandscapeSettings ToSettings(LandscapeSettingsRecord record) => new()
    {
        ProfileName = record.ProfileName,
        ChunkWorldSize = (float)record.ChunkWorldSize,
        ChunkHeightResolution = record.ChunkHeightResolution,
        ChunkAlphaResolution = record.ChunkAlphaResolution,
        ChunkHoleResolution = record.ChunkHoleResolution,
        HeightEncoding = (HeightEncoding)record.HeightEncoding,
        HeightOffset = (float)record.HeightOffset,
        HeightScale = (float)record.HeightScale,
        AlphaBitDepth = record.AlphaBitDepth,
        AlphaNormalized = record.AlphaNormalized,
        OriginChunkX = record.OriginChunkX,
        OriginChunkY = record.OriginChunkY,
        ChunkLimit = record.ChunkLimit,
        TextureLimit = record.TextureLimit,
        FallbackMaterialId = record.FallbackMaterialId,
    };

    private static void WriteRecord(LandscapeSettings settings, LandscapeSettingsRecord record)
    {
        record.ProfileName = settings.ProfileName;
        record.ChunkWorldSize = settings.ChunkWorldSize;
        record.ChunkHeightResolution = settings.ChunkHeightResolution;
        record.ChunkAlphaResolution = settings.ChunkAlphaResolution;
        record.ChunkHoleResolution = settings.ChunkHoleResolution;
        record.HeightEncoding = (int)settings.HeightEncoding;
        record.HeightOffset = settings.HeightOffset;
        record.HeightScale = settings.HeightScale;
        record.AlphaBitDepth = settings.AlphaBitDepth;
        record.AlphaNormalized = settings.AlphaNormalized;
        record.OriginChunkX = settings.OriginChunkX;
        record.OriginChunkY = settings.OriginChunkY;
        record.ChunkLimit = settings.ChunkLimit;
        record.TextureLimit = settings.TextureLimit;
        record.FallbackMaterialId = settings.FallbackMaterialId;
    }
}
