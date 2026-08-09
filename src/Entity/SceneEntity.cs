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

    /// <summary>The map this entity lives in. Streaming loads and unloads entities per map.</summary>
    public MapId Map { get; set; } = new(0);

    /// <summary>How the entity may rotate about itself; the object tool honours this.</summary>
    public abstract SelfRotation SelfRotation { get; }

    /// <summary>Selection bounds in the entity's local space (picking, outlines, marquee).</summary>
    public abstract Aabb LocalBounds { get; }

    /// <summary>
    /// <see cref="LocalBounds"/> placed by <see cref="Transform"/>, enclosed axis-aligned. This is the
    /// entity's extent in the world: what streaming and the landscape system query against, because an
    /// entity is in range when its bounds overlap a region, not when its origin happens to fall inside
    /// one. Factories persist it so that test can run in the database.
    /// </summary>
    public Aabb WorldBounds => Transform * LocalBounds;

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

    public void CreateRepresentation(Node parent)
    {
        if (Node != null)
        {
            return;
        }

        Node = BuildNode();
        parent.AddChild(Node);
        Node.GlobalTransform = _transform;
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
