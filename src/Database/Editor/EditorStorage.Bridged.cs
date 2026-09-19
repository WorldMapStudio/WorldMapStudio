using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>An entity stored in another table, named by the (<paramref name="Source"/>, <paramref name="Key"/>)
/// its factory gave it — see <see cref="ISceneEntityFactory.BridgeSource"/>.</summary>
public readonly record struct BridgedEntity(SceneEntity Entity, string Source, long Key);

/// <summary>What <see cref="EditorStorage.CommitBridgedAsync"/> did, for the caller to apply on the main
/// thread: entity state to write back, and the changes to the in-memory <see cref="EntityBridgeIndex"/>.</summary>
public sealed class BridgeCommitResult
{
    public List<Action> WriteBacks { get; } = [];

    public List<(string Source, long Key, int EntityId)> Added { get; } = [];

    public List<(string Source, long Key)> Removed { get; } = [];
}

public sealed partial class EditorStorage
{
    /// <summary>
    /// The attachment pass of a commit: writes the tags and attached components of entities that live in
    /// another storage, in one transaction. Runs after those entities' own rows are committed, so the
    /// keys a database assigned on insert are known.
    ///
    /// <paramref name="saves"/> get a bridge row (created on first use) with their tags diffed against
    /// what was last persisted and their components restaged. <paramref name="removals"/> lose their
    /// bridge row, and the foreign-key cascades take every tag and component row with it — used both when
    /// an entity is deleted and when it no longer carries any editor data, which keeps
    /// <see cref="EntityBridgeIndex"/> sparse.
    /// </summary>
    public async Task<BridgeCommitResult> CommitBridgedAsync(
        IReadOnlyList<BridgedEntity> saves, IReadOnlyList<BridgedEntity> removals)
    {
        var result = new BridgeCommitResult();
        if (saves.Count == 0 && removals.Count == 0)
        {
            return result;
        }

        using IDisposable write = await Lock.WriterAsync().ConfigureAwait(false);
        await using EditorDbContext context = CreateContext();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync().ConfigureAwait(false);

        foreach (BridgedEntity removal in removals)
        {
            if (removal.Entity.RecordId is int id)
            {
                context.Entities.Remove(new EntityRecord { Id = id });
                result.Removed.Add((removal.Source, removal.Key));
                result.WriteBacks.Add(() =>
                {
                    removal.Entity.RecordId = null;
                    removal.Entity.PersistedTags = EntityTagSet.Empty;
                });
            }
        }

        if (saves.Any(save => save.Entity.RecordId is null))
        {
            await EntityIds.SeedAsync(context).ConfigureAwait(false);
        }

        foreach (BridgedEntity save in saves)
        {
            SceneEntity entity = save.Entity;
            bool isNew = entity.RecordId is null;
            int id = entity.RecordId ?? EntityIds.Next();
            var identity = new EntityRecord { Id = id, Source = save.Source, SourceKey = save.Key };

            if (isNew)
            {
                // A row left behind by something deleted outside the editor would collide with this key.
                // Whatever it carried belongs to that other entity, so it goes.
                await context.Entities
                    .Where(record => record.Source == save.Source && record.SourceKey == save.Key)
                    .ExecuteDeleteAsync()
                    .ConfigureAwait(false);
                context.Entities.Add(identity);
                result.Added.Add((save.Source, save.Key, id));
            }

            Attachments.StageComponents(context, entity, identity);
            Action tagsWriteBack = await Attachments.StageTagsTolerantAsync(context, entity, id).ConfigureAwait(false);
            result.WriteBacks.Add(() =>
            {
                entity.RecordId = id;
                tagsWriteBack();
            });
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return result;
    }
}
