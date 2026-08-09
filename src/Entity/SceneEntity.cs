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
    /// True when this entity is positioned only by its horizontal coordinates and derives its visible
    /// height from the terrain. The stored world Y is ignored.
    /// </summary>
    public virtual bool UsesTerrainHeight => false;

    /// <summary>
    /// <see cref="LocalBounds"/> placed by <see cref="Transform"/>, enclosed axis-aligned. This is the
    /// entity's extent in the world: what streaming and the landscape system query against, because an
    /// entity is in range when its bounds overlap a region, not when its origin happens to fall inside
    /// one. Factories persist it so that test can run in the database.
    /// </summary>
    public Aabb WorldBounds => Transform * LocalBounds;

    public bool IsRepresented => Node != null;

    /// <summary>
    /// World placement. Setting it moves the live representation, if any.
    ///
    /// The field is the truth and the node is a mirror of it, never read back. It used to be the
    /// other way around whenever a representation existed, which made this getter a Godot node access
    /// — and background chunk building reads deformer transforms, so a rebuild racing a gizmo drag
    /// was touching a live node off the main thread. Nothing writes the node's transform except the
    /// two places below, so the mirror cannot drift.
    /// </summary>
    public Transform3D Transform
    {
        get => _transform;
        set
        {
            _transform = SanitizeTransform(value);
            if (Node != null)
            {
                Node.GlobalTransform = _transform;
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

        // Nothing to read back: the node only ever mirrored _transform.
        Node.QueueFree();
        Node = null;
    }

    /// <summary>Builds the entity's viewport node. Called on the main thread.</summary>
    protected abstract Node3D BuildNode();

    /// <summary>Reacts to a change in selection state (e.g. highlight). Default does nothing.</summary>
    public virtual void OnSelectionChanged(bool selected) { }

    protected virtual Transform3D SanitizeTransform(Transform3D transform)
    {
        if (UsesTerrainHeight)
        {
            transform.Origin = new Vector3(transform.Origin.X, 0.0f, transform.Origin.Z);
        }

        return transform;
    }
}
