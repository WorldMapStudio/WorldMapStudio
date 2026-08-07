namespace WorldMapStudio;

/// <summary>Base for all entity kinds, carrying only a runtime identity.</summary>
public abstract class Entity : IEntity
{
    public EntityId Id { get; } = EntityId.Next();
}
