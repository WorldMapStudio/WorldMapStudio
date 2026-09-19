using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

public sealed partial class DatabaseSystem
{
    /// <summary>
    /// The attachment pass of <see cref="Persist"/>. For each pinned entity that a bridging factory owns:
    /// one with no editor data and no bridge row is skipped without reading anything, so moving five
    /// thousand creatures costs nothing here; one that carries tags or attached components gets a bridge
    /// row and its data written; one that no longer carries any, or that was deleted, has its bridge row
    /// removed.
    /// </summary>
    private void CommitBridged(EditSession session, HashSet<Storage> failed)
    {
        if (Storages.OfType<EditorStorage>().FirstOrDefault() is not { } editor)
        {
            return;
        }

        var saves = new List<BridgedEntity>();
        var removals = new List<BridgedEntity>();
        foreach (IEntity pinned in session.Pinned)
        {
            if (pinned is not SceneEntity entity || entity is IDerivedEntity)
            {
                continue;
            }

            if (SceneSources.FactoryFor(entity) is not { BridgeSource: { } source } factory
                || SceneSources.StorageOf(entity) is not { } owner
                || failed.Contains(owner))
            {
                continue;
            }

            long? key = factory.PersistentKey(entity);
            bool hasData = !entity.Tags.IsEmpty || entity.AttachedComponents.Any();

            if (!IsLoaded(entity) || (entity.RecordId is not null && !hasData))
            {
                if (entity.RecordId is not null && key is long removedKey)
                {
                    removals.Add(new BridgedEntity(entity, source, removedKey));
                }

                continue;
            }

            if (entity.RecordId is null && !hasData)
            {
                continue;
            }

            if (key is not long savedKey)
            {
                GD.PushWarning($"[Database] '{entity.DisplayName}' has no stored key after its own commit, so its tags and components were not saved.");
                continue;
            }

            saves.Add(new BridgedEntity(entity, source, savedKey));
        }

        if (saves.Count == 0 && removals.Count == 0)
        {
            return;
        }

        try
        {
            BridgeCommitResult result = BlockingWork.Run(() => editor.CommitBridgedAsync(saves, removals));
            foreach (Action writeBack in result.WriteBacks)
            {
                writeBack();
            }

            Context.Bridge.Apply(result.Added, result.Removed);
        }
        catch (Exception e)
        {
            GD.PushError($"[Database] Saving tags and components of bridged entities failed: {e.Message}");
        }
    }
}
