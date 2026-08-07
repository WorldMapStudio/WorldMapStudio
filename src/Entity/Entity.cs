namespace WorldMapStudio;

/// <summary>Base for all entity kinds, carrying only a runtime identity.</summary>
public abstract class Entity : IEntity
{
    public EntityId Id { get; } = EntityId.Next();

    /// <summary>Label shown in the outline and inspector. Override for something friendlier.</summary>
    public virtual string DisplayName => $"{GetType().Name} {Id}";
}
