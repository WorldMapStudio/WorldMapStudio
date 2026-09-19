using Godot;

namespace WorldMapStudio;

/// <summary>
/// An entity authored in the editor and owned by one editor map, stored in the editor's own tables.
/// What the Scene menu, scripts, prefabs and the clipboard create.
/// </summary>
public sealed class MapSceneEntity : SceneEntity
{
    /// <summary>Copies tags and components but not <see cref="SceneEntity.RecordId"/>.</summary>
    public override SceneEntity Clone()
    {
        var clone = new MapSceneEntity { Name = Name, Map = Map };
        foreach (SceneComponent component in AttachedComponents)
        {
            clone.AddComponent(component.Clone());
        }

        clone.Tags = Tags;
        clone.Transform = Transform;
        return clone;
    }
}
