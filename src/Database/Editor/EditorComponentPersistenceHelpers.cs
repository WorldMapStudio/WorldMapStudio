using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>
/// Generic insert/update/delete helpers for a component's single-row-per-entity table, shared by
/// every <see cref="ISceneComponentPersistence"/> implementation. Assumes an <c>int EntityId</c>
/// property, matching the primary-key convention every scene-component table uses.
/// </summary>
public static class EditorComponentPersistenceHelpers
{
    private const string EntityIdProperty = "EntityId";

    public static void StageRow<TRecord>(EditorDbContext context, TRecord row, int? entityId) where TRecord : class
    {
        DbSet<TRecord> set = context.Set<TRecord>();
        if (entityId == null)
        {
            set.Add(row);
            return;
        }

        bool exists = set.Any(record => EF.Property<int>(record, EntityIdProperty) == entityId.Value);
        if (exists)
        {
            set.Update(row);
        }
        else
        {
            set.Add(row);
        }
    }

    public static void StageDelete<TRecord>(EditorDbContext context, int entityId) where TRecord : class, new()
    {
        DbSet<TRecord> set = context.Set<TRecord>();
        if (!set.Any(record => EF.Property<int>(record, EntityIdProperty) == entityId))
        {
            return;
        }

        var row = new TRecord();
        typeof(TRecord).GetProperty(EntityIdProperty)!.SetValue(row, entityId);
        set.Remove(row);
    }
}
