using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Godot;
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

    /// <summary>Deletes a component's single-row-per-entity table for every entity on <paramref name="map"/>
    /// — the one-line body most <see cref="ISceneComponentPersistence.DeleteForMapAsync"/> implementations
    /// need. A component with a child table deletes that first, then calls this for its own table.</summary>
    public static Task DeleteForMapAsync<TRecord>(EditorStorage storage, EditorDbContext context, DbTransaction transaction, MapId map)
        where TRecord : class =>
        storage.DeleteForMapEntitiesAsync<TRecord>(context, transaction, EntityIdProperty, map);

    /// <summary>A stored entity row as the id/map/world-bounds triple
    /// <see cref="IResourceReferencingPersistence.ReferencingBoundsAsync"/> returns.</summary>
    public static (int EntityId, MapId Map, Aabb Bounds) ToMapBounds(SceneEntityRecord entity) => (
        entity.Id,
        new MapId(entity.MapId),
        new Aabb(
            new Vector3((float)entity.MinX, (float)entity.MinY, (float)entity.MinZ),
            new Vector3(
                (float)(entity.MaxX - entity.MinX),
                (float)(entity.MaxY - entity.MinY),
                (float)(entity.MaxZ - entity.MinZ))));
}
