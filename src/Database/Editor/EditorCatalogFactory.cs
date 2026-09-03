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
    where TEntity : CatalogEntity, IKeyedCatalogEntity
    where TRecord : class, IKeyedRecord, new()
{
    protected EditorCatalogFactory(EditorStorage storage)
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

    public async Task<IReadOnlyList<CatalogEntity>> LoadAllAsync()
    {
        await using EditorDbContext context = Storage.CreateContext();
        List<TRecord> rows = await Set(context).AsNoTracking().ToListAsync().ConfigureAwait(false);
        return rows.Select(row =>
        {
            TEntity entity = ToEntity(row);
            entity.IsSaved = true;
            return (CatalogEntity)entity;
        }).ToList();
    }

    public Action Stage(DbContext context, IEntity entity)
    {
        var db = (EditorDbContext)context;
        var typed = (TEntity)entity;

        var record = new TRecord { Id = typed.RecordId ?? 0 };
        WriteRecord(typed, record);

        // Ids are assigned at creation, so having one no longer means a row exists — IsSaved is what
        // separates an insert from an update.
        if (typed.IsSaved)
        {
            Set(db).Update(record);
        }
        else
        {
            Set(db).Add(record);
        }

        return () => typed.IsSaved = true;
    }

    public void StageDelete(DbContext context, IEntity entity)
    {
        var db = (EditorDbContext)context;
        var typed = (TEntity)entity;

        // A never-saved entity has no row to remove; its id was only ever an in-memory reference.
        if (typed.IsSaved && typed.RecordId is int id)
        {
            Set(db).Remove(new TRecord { Id = id });
            typed.IsSaved = false;
        }
    }

    /// <summary>The table this catalog lives in.</summary>
    protected abstract DbSet<TRecord> Set(EditorDbContext context);

    protected abstract TEntity ToEntity(TRecord record);

    protected abstract void WriteRecord(TEntity entity, TRecord record);
}
