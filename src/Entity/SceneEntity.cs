#nullable enable
using Godot;

namespace WorldMapStudio;

/// <summary>
/// An entity placed in a map: it has a world transform and bounds, and owns its Godot
/// representation in the viewport. Streamed in and out as the user moves around the map.
/// </summary>
public abstract class SceneEntity : Entity
{
    private Transform3D _transform = Transform3D.Identity;

    /// <summary>The representation node while loaded into a viewport, otherwise null.</summary>
    protected Node3D? Node { get; private set; }

    /// <summary>How the entity may rotate about itself; the object tool honours this.</summary>
    public abstract SelfRotation SelfRotation { get; }

    /// <summary>Selection bounds in the entity's local space (picking, outlines, marquee).</summary>
    public abstract Aabb LocalBounds { get; }

    public bool IsRepresented => Node != null;

    /// <summary>World placement. Setting it moves the live representation, if any.</summary>
    public Transform3D Transform
    {
        get => Node?.GlobalTransform ?? _transform;
        set
        {
            _transform = value;
            if (Node != null)
            {
                Node.GlobalTransform = value;
            }
        }
    }

    public void CreateRepresentation(Node3D parent)
    {
        if (Node != null)
        {
            return;
        }

        Node = BuildNode();
        Node.GlobalTransform = _transform;
        parent.AddChild(Node);
    }

    public void DestroyRepresentation()
    {
        if (Node == null)
        {
            return;
        }

        _transform = Node.GlobalTransform;
        Node.QueueFree();
        Node = null;
    }

    /// <summary>Builds the entity's viewport node. Called on the main thread.</summary>
    protected abstract Node3D BuildNode();

    /// <summary>Reacts to a change in selection state (e.g. highlight). Default does nothing.</summary>
    public virtual void OnSelectionChanged(bool selected) { }
}
