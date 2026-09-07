using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

public sealed partial class EditorDbContext
{
    public DbSet<MapRecord> Maps => Set<MapRecord>();
}

/// <summary>
/// The built-in map source: maps the Editor storage's <c>maps</c> table to and from <see cref="Map"/>.
/// Unlike the scene factories (whose scans are locked by the streaming system), it takes the storage's
/// reader/writer lock itself, since the map system is its only caller.
/// </summary>
[Subsystem(nameof(EditorStorage))]
public sealed class EditorMapSource : IMapSource, ITableConfiguration
{
    private readonly EditorStorage _storage;

    public float Priority => 0f;

    public bool CanEdit => true;

    public EditorMapSource(EditorStorage storage)
    {
        _storage = storage;
    }

    public void Configure(ModelBuilder model)
    {
        model.Entity<MapRecord>(entity =>
        {
            entity.ToTable("wms_maps");
            entity.HasKey(record => record.Id);

            // The map id is the user's own (it matches the game's map ids), not a generated key.
            entity.Property(record => record.Id).ValueGeneratedNever();
        });
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

    public async Task RenameAsync(Map map)
    {
        using var write = await _storage.Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = _storage.CreateContext();
        context.Maps.Update(new MapRecord { Id = map.Id.Value, Name = map.Name });
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task DeleteAsync(Map map)
    {
        using var write = await _storage.Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = _storage.CreateContext();
        context.Maps.Remove(new MapRecord { Id = map.Id.Value });
        await context.SaveChangesAsync().ConfigureAwait(false);
    }
}
