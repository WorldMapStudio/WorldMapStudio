using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>Removes a scene entity from the registry (and restores it on undo). Deleted from the
/// database on commit, since a pinned entity no longer in the registry reads as a deletion.</summary>
public sealed class DeleteEntityCommand(SceneEntityRegistry scene, SceneEntity entity) : IEditCommand, IChunkChangeCommand
{
    public IReadOnlyList<IEntity> Targets { get; } = new IEntity[] { entity };

    public IReadOnlyList<ChunkChangeImpact> ChunkImpacts { get; } =
        [new ChunkChangeImpact(entity, ChunkChangeSnapshot.Capture(entity), null)];

    public string Description => $"Delete {entity.DisplayName}";

    public void Apply() => scene.Remove(entity);

    public void Revert() => scene.Add(entity);
}
