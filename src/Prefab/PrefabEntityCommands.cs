using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// Adds a prefab template entity to the scene registry, marked peripheral so it persists like any
/// other scene entity (see <see cref="DatabaseSystem"/>'s save-vs-delete check) but is never drawn,
/// listed or picked. The counterpart of <see cref="CreateEntityCommand"/> for library entities —
/// kept separate rather than reused because a template on <see cref="PrefabSystem.LibraryMap"/> never
/// has landscape-chunk impact, so it skips <see cref="IChunkChangeCommand"/> entirely.
/// </summary>
public sealed class CreateLibraryEntityCommand(SceneEntityRegistry scene, SceneEntity entity) : IEditCommand
{
    public IReadOnlyList<IEntity> Targets { get; } = new IEntity[] { entity };

    public string Description => $"Save {entity.DisplayName}";

    public void Apply()
    {
        scene.Add(entity);
        scene.SetPeripheral(entity, true);
    }

    public void Revert() => scene.Remove(entity);
}

/// <summary>Removes a prefab template entity from the scene registry (and restores it, still
/// peripheral, on undo). The library counterpart of <see cref="DeleteEntityCommand"/>.</summary>
public sealed class DeleteLibraryEntityCommand(SceneEntityRegistry scene, SceneEntity entity) : IEditCommand
{
    public IReadOnlyList<IEntity> Targets { get; } = new IEntity[] { entity };

    public string Description => $"Delete {entity.DisplayName}";

    public void Apply() => scene.Remove(entity);

    public void Revert()
    {
        scene.Add(entity);
        scene.SetPeripheral(entity, true);
    }
}
