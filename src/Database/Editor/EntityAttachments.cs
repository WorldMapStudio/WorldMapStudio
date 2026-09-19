using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>An entity carrying a tag: one row of <c>wms_entity_tags</c>.</summary>
public sealed class EntityTagRecord
{
    public int EntityId { get; set; }

    public int TagId { get; set; }
}

public sealed partial class EditorDbContext
{
    public DbSet<EntityTagRecord> EntityTags => Set<EntityTagRecord>();
}

/// <summary>
/// The single place that reads and stages the editor-side data an entity carries whatever table owns
/// the entity itself: its tags and its attached components. Native entities go through it as part of
/// their own scan and commit; a bridged entity goes through it after its own factory has run.
/// </summary>
public sealed class EntityAttachments(EditorStorage storage)
{
    /// <summary>
    /// Loads tags and every component kind for the entities in <paramref name="ids"/>, attaching them to
    /// the matching entries of <paramref name="byId"/>. Tags are set before the entity is handed to the
    /// registry, so loading bumps nothing.
    /// </summary>
    public async Task LoadAsync(
        EditorDbContext context,
        IReadOnlyDictionary<int, SceneEntity> byId,
        IReadOnlyList<int> ids,
        SceneEntityScanCatalog catalog)
    {
        long clock = DiagnosticLog.Start();
        await LoadTagsAsync(context, byId, ids).ConfigureAwait(false);
        DiagnosticLog.Log($"    tags: {DiagnosticLog.MillisecondsSince(clock):F0}ms");

        foreach (ISceneComponentPersistence persistence in storage.ComponentPersistence)
        {
            using IDisposable scope = DiagnosticLog.Scope(persistence.GetType().Name);
            clock = DiagnosticLog.Start();
            await persistence.LoadAsync(context, byId, ids, catalog).ConfigureAwait(false);
            DiagnosticLog.Log($"    {persistence.GetType().Name}: {DiagnosticLog.MillisecondsSince(clock):F0}ms");
        }
    }

    /// <summary>
    /// Stages the entity's tag changes against <see cref="SceneEntity.PersistedTags"/> — inserts and
    /// deletes only, so a commit never reads the table — and returns the write-back that records what
    /// was staged as persisted.
    /// </summary>
    public Action StageTags(EditorDbContext context, SceneEntity entity, int entityId)
    {
        EntityTagSet staged = entity.Tags;
        EntityTagSet persisted = entity.PersistedTags;

        foreach (int tagId in staged)
        {
            if (!persisted.Contains(tagId))
            {
                context.EntityTags.Add(new EntityTagRecord { EntityId = entityId, TagId = tagId });
            }
        }

        foreach (int tagId in persisted)
        {
            if (!staged.Contains(tagId))
            {
                context.EntityTags.Remove(new EntityTagRecord { EntityId = entityId, TagId = tagId });
            }
        }

        return () => entity.PersistedTags = staged;
    }

    /// <summary>Stages every component kind's rows for the entity.</summary>
    public void StageComponents(EditorDbContext context, SceneEntity entity, EntityRecord identity)
    {
        foreach (ISceneComponentPersistence persistence in storage.ComponentPersistence)
        {
            persistence.Stage(context, entity, identity);
        }
    }

    private static async Task LoadTagsAsync(EditorDbContext context, IReadOnlyDictionary<int, SceneEntity> byId, IReadOnlyList<int> ids)
    {
        List<EntityTagRecord> rows = await context.EntityTags.AsNoTracking()
            .Where(record => ids.Contains(record.EntityId))
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (IGrouping<int, EntityTagRecord> group in rows.GroupBy(row => row.EntityId))
        {
            if (byId.TryGetValue(group.Key, out SceneEntity? entity))
            {
                EntityTagSet tags = EntityTagSet.From(group.Select(row => row.TagId));
                entity.Tags = tags;
                entity.PersistedTags = tags;
            }
        }
    }
}
