using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>A chunk and when an edit last touched it, as JS sees it. Times are ISO-8601 UTC strings,
/// since that is what round-trips through a watermark stored as text.</summary>
public sealed class ChunkChangeDescriptor
{
    internal ChunkChangeDescriptor(ChunkChange change)
    {
        MapId = change.Map.Value;
        X = change.Coord.X;
        Y = change.Coord.Y;
        LastEditedUtc = ChunkChangeScriptApi.ToIso(change.LastEditedUtc);
    }

    [ScriptProperty]
    public int MapId { get; }

    [ScriptProperty]
    public int X { get; }

    [ScriptProperty]
    public int Y { get; }

    [ScriptProperty]
    public string LastEditedUtc { get; }
}

/// <summary>
/// Read access to the chunk change log, exposed to JS as <c>wms.chunks</c>. The whole model is
/// "everything that changed after this moment": keep the timestamp a query returns and pass it back
/// next time.
/// </summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class ChunkChangeScriptApi : IScriptModule
{
    private readonly EditorContext _context;

    public string Name => "chunks";

    public float Priority => 0f;

    public ChunkChangeScriptApi(ScriptingSystem system)
    {
        _context = system.Context;
    }

    /// <summary>Chunks edited after <paramref name="sinceUtc"/> (ISO-8601), optionally on one map.
    /// Pass null or an empty string for "everything ever edited".</summary>
    [ScriptFunction]
    public async Task<ChunkChangeDescriptor[]> ChangedSince(string? sinceUtc, int? mapId = null)
    {
        DateTime since = ParseIso(sinceUtc);
        return await Task.Run(async () =>
        {
            var changes = await _context.ChunkChanges.ChangedSinceAsync(since, ToMap(mapId)).ConfigureAwait(false);
            return changes.Select(change => new ChunkChangeDescriptor(change)).ToArray();
        }).ConfigureAwait(false);
    }

    /// <summary>Every edited chunk in a coordinate rectangle, regardless of when.</summary>
    [ScriptFunction]
    public async Task<ChunkChangeDescriptor[]> InRange(int mapId, int minX, int minY, int maxX, int maxY)
    {
        var range = new ChunkRange(new MapId(mapId), new ChunkCoord(minX, minY), new ChunkCoord(maxX, maxY));
        return await Task.Run(async () =>
        {
            var changes = await _context.ChunkChanges.InRangeAsync(range).ConfigureAwait(false);
            return changes.Select(change => new ChunkChangeDescriptor(change)).ToArray();
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// The newest edit time on record, or null when nothing has ever been edited. This, not the wall
    /// clock, is what a consumer stores as its watermark: a commit landing mid-run would fall on the
    /// wrong side of <c>Date.now()</c> and be skipped forever, while taking the value from the data
    /// makes the worst case a redundant reprocess.
    /// </summary>
    [ScriptFunction]
    public async Task<string?> LatestEdit(int? mapId = null)
    {
        DateTime? latest = await Task.Run(() => _context.ChunkChanges.LatestEditUtcAsync(ToMap(mapId))).ConfigureAwait(false);
        return latest is { } value ? ToIso(value) : null;
    }

    internal static string ToIso(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    internal static DateTime ParseIso(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? DateTime.MinValue
            : DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    private static MapId? ToMap(int? mapId) => mapId is { } id ? new MapId(id) : null;
}
