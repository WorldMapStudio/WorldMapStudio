using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>Adds a scene entity to the registry (and removes it on undo). Persisted on commit.</summary>
public sealed class CreateEntityCommand(SceneEntityRegistry scene, SceneEntity entity) : IEditCommand, IChunkChangeCommand
{
    public IReadOnlyList<IEntity> Targets { get; } = new IEntity[] { entity };

    public IReadOnlyList<ChunkChangeImpact> ChunkImpacts { get; } =
        [new ChunkChangeImpact(entity, null, ChunkChangeSnapshot.Capture(entity))];

    public string Description => $"Create {entity.DisplayName}";

    public void Apply() => scene.Add(entity);

    public void Revert() => scene.Remove(entity);
}
