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
/// A catalog that spans several tables, or lives in tables we do not control, implements
/// <see cref="ICatalogEntityFactory"/> directly instead — that freedom is the point of factories.
/// </summary>
public abstract class EditorCatalogFactory<TEntity, TRecord> : ICatalogEntityFactory
    where TEntity : CatalogEntity
    where TRecord : class, IKeyedRecord, new()
{
    protected EditorCatalogFactory(EditorStorage storage)
    {
        Storage = storage;
    }

    protected EditorStorage Storage { get; }

    public virtual float Priority => 0.0f;

    public Type EntityType => typeof(TEntity);

    public bool Handles(IEntity entity) => entity is TEntity;

    public async Task<IReadOnlyList<CatalogEntity>> LoadAllAsync()
    {
        await using EditorDbContext context = Storage.CreateContext();
        List<TRecord> rows = await Set(context).AsNoTracking().ToListAsync().ConfigureAwait(false);
        return rows.Select(row => (CatalogEntity)ToEntity(row)).ToList();
    }

    public Action Stage(DbContext context, IEntity entity)
    {
        var db = (EditorDbContext)context;
        var typed = (TEntity)entity;
        bool isNew = RecordId(typed) is null;

        var record = new TRecord();
        if (RecordId(typed) is int id)
        {
            record.Id = id;
        }

        WriteRecord(typed, record);

        if (isNew)
        {
            Set(db).Add(record);
        }
        else
        {
            Set(db).Update(record);
        }

        // After SaveChanges, EF has populated the generated key on inserts; copy it back.
        return () => SetRecordId(typed, record.Id);
    }

    public void StageDelete(DbContext context, IEntity entity)
    {
        var db = (EditorDbContext)context;
        if (RecordId((TEntity)entity) is int id)
        {
            Set(db).Remove(new TRecord { Id = id });
        }
    }

    /// <summary>The table this catalog lives in.</summary>
    protected abstract DbSet<TRecord> Set(EditorDbContext context);

    /// <summary>The entity's row key, or null if it was never saved.</summary>
    protected abstract int? RecordId(TEntity entity);

    protected abstract void SetRecordId(TEntity entity, int id);

    protected abstract TEntity ToEntity(TRecord record);

    protected abstract void WriteRecord(TEntity entity, TRecord record);
}
