using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>A row in one of the Editor storage's own tables, keyed by a generated integer.</summary>
public interface IKeyedRecord
{
    int Id { get; set; }
}

/// <summary>
/// Shared plumbing for a catalog entity living in one Editor-storage table with a generated key. The
/// staging protocol is identical for every such catalog, so a concrete factory supplies only the
/// mapping: which table, where the key lives on the entity, and how a row and an entity convert.
///
/// Deliberately has no bulk load — that is what separates <see cref="EditorCatalogFactory{TEntity,TRecord}"/>
/// (eager) from <see cref="EditorLazyCatalogFactory{TEntity,TRecord}"/> (loaded on demand); this base is
/// everything both share.
///
/// A catalog that spans several tables, or lives in tables we do not control, implements
/// <see cref="ICatalogEntityFactory"/> or <see cref="ILazyCatalogEntityFactory"/> directly instead —
/// that freedom is the point of factories.
/// </summary>
public abstract class EditorKeyedCatalogFactory<TEntity, TRecord> : IEntityFactory, IRecordIdSource
    where TEntity : CatalogEntity, IKeyedCatalogEntity
    where TRecord : class, IKeyedRecord, new()
{
    protected EditorKeyedCatalogFactory(EditorStorage storage)
    {
        Storage = storage;
    }

    protected EditorStorage Storage { get; }

    public virtual float Priority => 0.0f;

    public Type EntityType => typeof(TEntity);

    /// <summary>The table this catalog lives in — the only thing that differs between one of these
    /// factories and another's <see cref="Configure"/>, since every keyed-record catalog shares the
    /// same editor-assigned, never-generated <c>Id</c> key.</summary>
    protected abstract string TableName { get; }

    public bool Handles(IEntity entity) => entity is TEntity;

    public void Configure(ModelBuilder model)
    {
        model.Entity<TRecord>(entity =>
        {
            entity.ToTable(TableName);
            entity.HasKey(record => record.Id);

            // The editor assigns catalog ids so entities can reference each other before a commit.
            entity.Property(record => record.Id).ValueGeneratedNever();
        });
    }

    /// <summary>Loads exactly the rows named by <paramref name="ids"/>, mapped the same way a bulk load
    /// would. Takes the caller's context rather than creating one, so it runs inside whatever
    /// transaction and reader lock the caller already holds — the scan that resolves a lazily-loaded
    /// reference this way is already inside both.</summary>
    public async Task<IReadOnlyList<CatalogEntity>> LoadByIdAsync(EditorDbContext context, IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        List<TRecord> rows = await context.Set<TRecord>().AsNoTracking()
            .Where(row => ids.Contains(row.Id)).ToListAsync().ConfigureAwait(false);

        return rows.Select(row =>
        {
            TEntity entity = ToEntity(row);
            entity.IsSaved = true;
            return (CatalogEntity)entity;
        }).ToList();
    }

    /// <summary>The highest row id stored for this type, or 0 if none. What
    /// <see cref="CatalogEntityRegistry.AssignId{TEntity}"/> seeds its high-water mark from.</summary>
    public async Task<int> MaxRecordIdAsync()
    {
        await using EditorDbContext context = Storage.CreateContext();
        return await context.Set<TRecord>().MaxAsync(record => (int?)record.Id).ConfigureAwait(false) ?? 0;
    }

    public Action Stage(DbContext context, IEntity entity)
    {
        var typed = (TEntity)entity;

        var record = new TRecord { Id = typed.RecordId ?? 0 };
        WriteRecord(typed, record);

        // Ids are assigned at creation, so having one no longer means a row exists — IsSaved is what
        // separates an insert from an update.
        if (typed.IsSaved)
        {
            context.Set<TRecord>().Update(record);
        }
        else
        {
            context.Set<TRecord>().Add(record);
        }

        return () => typed.IsSaved = true;
    }

    public void StageDelete(DbContext context, IEntity entity)
    {
        var typed = (TEntity)entity;

        // A never-saved entity has no row to remove; its id was only ever an in-memory reference.
        if (typed.IsSaved && typed.RecordId is int id)
        {
            context.Set<TRecord>().Remove(new TRecord { Id = id });
            typed.IsSaved = false;
        }
    }

    protected abstract TEntity ToEntity(TRecord record);

    protected abstract void WriteRecord(TEntity entity, TRecord record);
}

/// <summary>
/// A <see cref="EditorKeyedCatalogFactory{TEntity,TRecord}"/> whose table is small enough to load whole. The default
/// base for a catalog — the split only matters to one that chooses
/// <see cref="EditorLazyCatalogFactory{TEntity,TRecord}"/>.
/// </summary>
public abstract class EditorCatalogFactory<TEntity, TRecord> : EditorKeyedCatalogFactory<TEntity, TRecord>, ICatalogEntityFactory
    where TEntity : CatalogEntity, IKeyedCatalogEntity
    where TRecord : class, IKeyedRecord, new()
{
    protected EditorCatalogFactory(EditorStorage storage) : base(storage)
    {
    }

    public async Task<IReadOnlyList<CatalogEntity>> LoadAllAsync()
    {
        await using EditorDbContext context = Storage.CreateContext();
        List<TRecord> rows = await context.Set<TRecord>().AsNoTracking().ToListAsync().ConfigureAwait(false);
        return rows.Select(row =>
        {
            TEntity entity = ToEntity(row);
            entity.IsSaved = true;
            return (CatalogEntity)entity;
        }).ToList();
    }
}

/// <summary>
/// A <see cref="EditorKeyedCatalogFactory{TEntity,TRecord}"/> for a table too large to load whole. Adds
/// nothing over the shared base but the marker interface — see <see cref="ILazyCatalogEntityFactory"/>
/// for why "load everything up front" deliberately has no equivalent here.
/// </summary>
public abstract class EditorLazyCatalogFactory<TEntity, TRecord> : EditorKeyedCatalogFactory<TEntity, TRecord>, ILazyCatalogEntityFactory
    where TEntity : CatalogEntity, IKeyedCatalogEntity
    where TRecord : class, IKeyedRecord, new()
{
    protected EditorLazyCatalogFactory(EditorStorage storage) : base(storage)
    {
    }
}
