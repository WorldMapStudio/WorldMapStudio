using Godot;

namespace WorldMapStudio;

/// <summary>
/// An entity authored in the editor and owned by one editor map, stored in the editor's own tables.
/// What the Scene menu, scripts, prefabs and the clipboard create.
/// </summary>
public sealed class MapSceneEntity : SceneEntity
{
    public override SceneEntity Clone()
    {
        var clone = new MapSceneEntity { Name = Name, Map = Map };
        foreach (SceneComponent component in Components)
        {
            clone.AddComponent(component.Clone());
        }

        clone.Transform = Transform;
        return clone;
    }
}
