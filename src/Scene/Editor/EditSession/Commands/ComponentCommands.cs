using System.Collections.Generic;

namespace WorldMapStudio;

public sealed class AddComponentCommand(SceneEntity entity, SceneComponent component) : IEditCommand, IChunkChangeCommand
{
    public IReadOnlyList<IEntity> Targets { get; } = new IEntity[] { entity };

    public IReadOnlyList<ChunkChangeImpact> ChunkImpacts { get; } =
    [
        new ChunkChangeImpact(
            entity,
            ChunkChangeSnapshot.Capture(entity),
            CaptureAfter(entity, component))
    ];

    public string Description => $"Add {component.DisplayName} to {entity.DisplayName}";

    public void Apply() => entity.AddComponent(component);

    public void Revert() => entity.RemoveComponent(component);

    private static ChunkChangeSnapshot CaptureAfter(SceneEntity entity, SceneComponent component)
    {
        Godot.Transform3D transform = entity.Transform;
        entity.AddComponent(component);
        ChunkChangeSnapshot snapshot = ChunkChangeSnapshot.Capture(entity);
        entity.RemoveComponent(component);
        entity.Transform = transform;
        return snapshot;
    }
}

public sealed class RemoveComponentCommand(SceneEntity entity, SceneComponent component) : IEditCommand, IChunkChangeCommand
{
    public IReadOnlyList<IEntity> Targets { get; } = new IEntity[] { entity };

    public IReadOnlyList<ChunkChangeImpact> ChunkImpacts { get; } =
    [
        new ChunkChangeImpact(
            entity,
            ChunkChangeSnapshot.Capture(entity),
            CaptureAfter(entity, component))
    ];

    public string Description => $"Remove {component.DisplayName} from {entity.DisplayName}";

    public void Apply() => entity.RemoveComponent(component);

    public void Revert() => entity.AddComponent(component);

    private static ChunkChangeSnapshot CaptureAfter(SceneEntity entity, SceneComponent component)
    {
        Godot.Transform3D transform = entity.Transform;
        entity.RemoveComponent(component);
        ChunkChangeSnapshot snapshot = ChunkChangeSnapshot.Capture(entity);
        entity.AddComponent(component);
        entity.Transform = transform;
        return snapshot;
    }
}
