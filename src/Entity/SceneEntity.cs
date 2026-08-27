using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// An entity placed in a map: it has a world transform and bounds, and owns its Godot
/// representation in the viewport. Streamed in and out as the user moves around the map.
/// </summary>
public class SceneEntity : Entity
{
    private Transform3D _transform = Transform3D.Identity;
    private readonly List<SceneComponent> _components = [];

    /// <summary>The representation node while loaded into a viewport, otherwise null.</summary>
    protected Node3D? Node { get; private set; }

    /// <summary>The map this entity lives in. Streaming loads and unloads entities per map.</summary>
    public MapId Map { get; set; } = new(0);

    /// <summary>Primary key of the backing row once persisted; null until first saved.</summary>
    public int? RecordId { get; set; }

    [ScriptProperty(Mutable = true)]
    public string Name { get; set; } = "Entity";

    /// <summary>How the entity may rotate about itself; the object tool honours this.</summary>
    public virtual SelfRotation SelfRotation
    {
        get
        {
            SelfRotation rotation = SelfRotation.Full;
            foreach (ITransformPolicy policy in Components.OfType<ITransformPolicy>())
            {
                if (policy.SelfRotation < rotation)
                {
                    rotation = policy.SelfRotation;
                }
            }

            return rotation;
        }
    }

    /// <summary>Selection bounds in the entity's local space (picking, outlines, marquee).</summary>
    public virtual Aabb LocalBounds => EffectiveLocalBounds;

    /// <summary>
    /// True when this entity is positioned only by its horizontal coordinates and derives its visible
    /// height from the terrain. The stored world Y is ignored.
    /// </summary>
    public virtual bool UsesTerrainHeight => Components.OfType<ITransformPolicy>().Any(policy => policy.UsesTerrainHeight);

    /// <summary>
    /// <see cref="LocalBounds"/> placed by <see cref="Transform"/>, enclosed axis-aligned. This is the
    /// entity's extent in the world: what streaming and the landscape system query against, because an
    /// entity is in range when its bounds overlap a region, not when its origin happens to fall inside
    /// one. Factories persist it so that test can run in the database.
    /// </summary>
    public Aabb WorldBounds => Transform * LocalBounds;

    public bool IsRepresented => Node != null;

    public IReadOnlyList<SceneComponent> Components => _components;

    public override string DisplayName => Name;

    /// <summary>The centered, per-axis maximum bounds contributed by this entity's components.</summary>
    public Aabb EffectiveLocalBounds
    {
        get
        {
            Vector3 size = Vector3.One;
            foreach (ISceneBoundsProvider provider in Components.OfType<ISceneBoundsProvider>())
            {
                Vector3 contribution = provider.LocalBounds.Size.Abs();
                size = new Vector3(
                    Mathf.Max(size.X, contribution.X),
                    Mathf.Max(size.Y, contribution.Y),
                    Mathf.Max(size.Z, contribution.Z));
            }

            return new Aabb(size * -0.5f, size);
        }
    }

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

    public T? Component<T>() where T : SceneComponent =>
        Components.OfType<T>().FirstOrDefault();

    public IEnumerable<T> ComponentsOf<T>() =>
        Components.OfType<T>();

    public void AddComponent(SceneComponent component)
    {
        if (component.Owner != null)
        {
            throw new InvalidOperationException("Component already belongs to an entity.");
        }

        component.Owner = this;
        _components.Add(component);
        RebuildRepresentation();
        Transform = Transform;
    }

    public bool RemoveComponent(SceneComponent component)
    {
        if (!_components.Remove(component))
        {
            return false;
        }

        component.Owner = null;
        RebuildRepresentation();
        Transform = Transform;
        return true;
    }

    public void LoadComponent(SceneComponent component)
    {
        component.Owner = this;
        _components.Add(component);
    }

    /// <summary>Builds the entity's viewport node. Called on the main thread.</summary>
    protected virtual Node3D BuildNode()
    {
        var root = new Node3D { Name = $"Entity{Id.Value}" };
        foreach (ISceneNodeComponent component in Components.OfType<ISceneNodeComponent>())
        {
            if (component.BuildNode() is { } child)
            {
                root.AddChild(child);
            }
        }

        return root;
    }

    /// <summary>Reacts to a change in selection state (e.g. highlight). Default does nothing.</summary>
    public virtual void OnSelectionChanged(bool selected) { }

    protected virtual Transform3D SanitizeTransform(Transform3D transform)
    {
        if (UsesTerrainHeight)
        {
            transform.Origin = new Vector3(transform.Origin.X, 0.0f, transform.Origin.Z);
        }

        if (SelfRotation == SelfRotation.HeightOnly)
        {
            float yaw = transform.Basis.GetEuler().Y;
            transform.Basis = new Basis(Vector3.Up, yaw);
        }
        else if (SelfRotation == SelfRotation.None)
        {
            transform.Basis = Basis.Identity;
        }

        return transform;
    }

    private void RebuildRepresentation()
    {
        if (Node?.GetParent() is not { } parent)
        {
            return;
        }

        DestroyRepresentation();
        CreateRepresentation(parent);
    }
}
