using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>Adds a scene entity to the registry (and removes it on undo). Persisted on commit.</summary>
public sealed class CreateEntityCommand(SceneEntityRegistry scene, SceneEntity entity) : IEditCommand
{
    public IReadOnlyList<IEntity> Targets { get; } = new IEntity[] { entity };

    public void Apply() => scene.Add(entity);

    public void Revert() => scene.Remove(entity);
}
