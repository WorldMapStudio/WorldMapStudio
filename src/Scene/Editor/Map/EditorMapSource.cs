using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>
/// The built-in map source: maps the Editor storage's <c>maps</c> table to and from <see cref="Map"/>.
/// Unlike the scene factories (whose scans are locked by the streaming system), it takes the storage's
/// reader/writer lock itself, since the map system is its only caller.
/// </summary>
[Subsystem(nameof(EditorStorage))]
public sealed class EditorMapSource : IMapSource
{
    private readonly EditorStorage _storage;

    public float Priority => 0f;

    public bool CanCreate => true;

    public EditorMapSource(EditorStorage storage)
    {
        _storage = storage;
    }

    public async Task<IReadOnlyList<Map>> LoadAsync()
    {
        using var read = await _storage.Lock.ReaderAsync().ConfigureAwait(false);
        await using EditorDbContext context = _storage.CreateContext();
        List<MapRecord> rows = await context.Maps.AsNoTracking()
            .OrderBy(record => record.Id)
            .ToListAsync()
            .ConfigureAwait(false);
        return rows.Select(record => new Map(new MapId(record.Id), record.Name)).ToList();
    }

    public async Task CreateAsync(Map map)
    {
        using var write = await _storage.Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = _storage.CreateContext();
        context.Maps.Add(new MapRecord { Id = map.Id.Value, Name = map.Name });
        await context.SaveChangesAsync().ConfigureAwait(false);
    }
}
