using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>
/// Persists one <see cref="SceneComponent"/> kind against <see cref="EditorStorage"/>. Self-registers
/// with [Subsystem(nameof(EditorStorage))], exactly like <see cref="ISceneComponentType"/> registers
/// with <see cref="SceneComponentRegistry"/> — a plugin declaring a component kind uses both seams
/// together: one for how it's created and edited, one for how it's stored.
///
/// Registered persisters contribute their tables into <see cref="EditorDbContext"/>'s model via
/// <see cref="Configure"/>. That only works because the set of persisters is fixed before the first
/// context is built (subsystems are constructed once, at startup): if that ever stopped being true, EF's
/// model cache (keyed only by <see cref="DbContextOptions"/> today) would need an
/// <c>IModelCacheKeyFactory</c> that folds the persister set in too.
/// </summary>
public interface ISceneComponentPersistence : ISubsystem
{
    /// <summary>Matches the component's <see cref="SceneComponent.TypeId"/>.</summary>
    string TypeId { get; }

    /// <summary>Declares this component's table(s) into the storage's EF model.</summary>
    void Configure(ModelBuilder model);

    /// <summary>
    /// Loads this component for every entity in <paramref name="ids"/> that has one, attaching it via
    /// <see cref="SceneEntity.LoadComponent"/> on the matching entry of <paramref name="byId"/>.
    ///
    /// <paramref name="catalog"/> is the scan's collector for any lazily-loaded catalog reference a
    /// component owns — most persisters have none and ignore it; see
    /// <see cref="ProceduralComponentPersistence"/> for the one that resolves models through it.
    /// </summary>
    Task LoadAsync(EditorDbContext context, IReadOnlyDictionary<int, SceneEntity> byId, IReadOnlyList<int> ids, SceneEntityScanCatalog catalog);

    /// <summary>
    /// Stages an insert or update of <paramref name="entity"/>'s component of this kind, or a delete if
    /// it no longer carries one. <paramref name="entityRow"/> is the entity's just-staged row, needed
    /// only for EF navigation fixup when the entity has no id yet.
    /// </summary>
    void Stage(EditorDbContext context, SceneEntity entity, SceneEntityRecord entityRow);

    /// <summary>Stages a delete of this component for an entity being deleted outright.</summary>
    void StageDelete(EditorDbContext context, int entityId);

    /// <summary>
    /// Deletes this component's rows for every entity on <paramref name="map"/>. Runs inside the
    /// delete transaction, with <paramref name="context"/>'s connection already open, and before the
    /// entity rows themselves go — see <see cref="SceneEntityFactory"/>'s <see cref="IMapScopedData"/>
    /// implementation, which is the only caller. Most implementations are one line calling
    /// <see cref="EditorStorage.DeleteForMapEntitiesAsync{TRecord}"/>; one with a child table (an entry
    /// list keyed on <c>EntityId</c>) deletes the child rows first.
    /// </summary>
    Task DeleteForMapAsync(EditorDbContext context, DbTransaction transaction, MapId map);
}
