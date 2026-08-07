namespace WorldMapStudio;

/// <summary>Anything the editor can select, edit and track through an edit session.</summary>
public interface IEntity
{
    EntityId Id { get; }
}
