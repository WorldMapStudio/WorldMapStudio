using System;

namespace WorldMapStudio;

/// <summary>
/// The insert/update/delete branching every <see cref="ISavedCatalogEntity"/> factory needs: insert if
/// never saved, update otherwise, and only delete a row that was actually saved. Core's
/// <c>EditorCatalogFactory&lt;,&gt;</c> already gives this for free to any catalog keyed by an
/// editor-allocated <see cref="IKeyedCatalogEntity.RecordId"/>; this is the same branching for a
/// catalog keyed by a game-authored value instead, which can't extend that generic base.
/// </summary>
public static class SavedEntityStaging
{
    /// <summary>Runs <paramref name="insert"/> or <paramref name="update"/> depending on
    /// <see cref="ISavedCatalogEntity.IsSaved"/>, and returns the commit callback that marks the entity
    /// saved — the same deferred-commit shape <see cref="ICatalogEntityFactory.Stage"/> already uses, so
    /// a failed transaction leaves <see cref="ISavedCatalogEntity.IsSaved"/> untouched.</summary>
    public static Action Stage(ISavedCatalogEntity entity, Action insert, Action update)
    {
        if (entity.IsSaved)
        {
            update();
        }
        else
        {
            insert();
        }

        return () => entity.IsSaved = true;
    }

    /// <summary>Runs <paramref name="delete"/> only if a row actually exists for the entity, and clears
    /// <see cref="ISavedCatalogEntity.IsSaved"/> once it does.</summary>
    public static void StageDelete(ISavedCatalogEntity entity, Action delete)
    {
        if (!entity.IsSaved)
        {
            return;
        }

        delete();
        entity.IsSaved = false;
    }
}
