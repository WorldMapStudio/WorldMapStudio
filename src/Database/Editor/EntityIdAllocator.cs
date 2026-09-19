using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>
/// Hands out ids for new <c>wms_entities</c> rows. Native entities and bridged ones share the id space,
/// so both commit paths draw from one high-water mark: seeded from the table's largest id once per
/// commit, then handed out sequentially so EF can batch the inserts into one multi-row statement instead
/// of one INSERT and one SELECT LAST_INSERT_ID() per row. The write lock the commit holds is what makes
/// the seed-then-allocate sequence safe.
/// </summary>
public sealed class EntityIdAllocator
{
    private int _highWater;

    /// <summary>Raises the high-water mark to the table's largest id. Never lowers it: ids already
    /// handed out this commit are not in the table yet.</summary>
    public async Task SeedAsync(EditorDbContext context)
    {
        int max = await context.Entities.AsNoTracking()
            .Select(record => (int?)record.Id)
            .MaxAsync()
            .ConfigureAwait(false) ?? 0;

        int current;
        do
        {
            current = Volatile.Read(ref _highWater);
        }
        while (max > current && Interlocked.CompareExchange(ref _highWater, max, current) != current);
    }

    public int Next() => Interlocked.Increment(ref _highWater);
}
