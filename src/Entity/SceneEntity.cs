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
    private readonly List<SceneEntity> _children = [];
    private readonly List<Node3D> _meshPickNodes = [];
    private SceneEntity? _parent;

    /// <summary>The representation node while loaded into a viewport, otherwise null.</summary>
    protected Node3D? Node { get; private set; }

    /// <summary>The map this entity lives in. Streaming loads and unloads entities per map.</summary>
    public MapId Map { get; set; } = new(0);

    /// <summary>Primary key of the backing row once persisted; null until first saved.</summary>
    public int? RecordId { get; set; }

    /// <summary>
    /// Primary key of the persisted parent row. Kept separately so streamed entities can remember
    /// relationships before every member has been resolved to a live object.
    /// </summary>
    public int? ParentRecordId { get; set; }

    /// <summary>
    /// Optional parent entity. Transforms are still stored in world space; the relationship controls
    /// editor grouping, loading, and parent-driven movement rather than Godot node parenting.
    /// </summary>
    public SceneEntity? Parent
    {
        get => _parent;
        set
        {
            if (ReferenceEquals(_parent, value))
            {
                return;
            }

            if (value != null && WouldCycle(value))
            {
                GD.PushError($"[Scene] Refusing to parent {DisplayName} to {value.DisplayName}: would create a cycle.");
                return;
            }

            _parent?._children.Remove(this);
            _parent = value;
            if (_parent != null && !_parent._children.Contains(this))
            {
                _parent._children.Add(this);
            }
        }
    }

    public IReadOnlyList<SceneEntity> Children => _children;

    /// <summary>Whether <paramref name="parent"/> is this entity itself or one of its own descendants.</summary>
    public bool WouldCycle(SceneEntity parent)
    {
        for (SceneEntity? current = parent; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, this))
            {
                return true;
            }
        }

        return false;
    }

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

    /// <summary>How the entity may be scaled about itself; the object tool honours this.</summary>
    public virtual SelfScale SelfScale
    {
        get
        {
            SelfScale scale = SelfScale.PerAxis;
            foreach (ITransformPolicy policy in Components.OfType<ITransformPolicy>())
            {
                if (policy.SelfScale < scale)
                {
                    scale = policy.SelfScale;
                }
            }

            return scale;
        }
    }

    /// <summary>Selection bounds in the entity's local space (picking, outlines, marquee).</summary>
    public virtual Aabb LocalBounds => EffectiveLocalBounds;

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
            using IEnumerator<ISceneBoundsProvider> providers = Components.OfType<ISceneBoundsProvider>().GetEnumerator();
            if (!providers.MoveNext())
            {
                return new Aabb(-Vector3.One * 0.5f, Vector3.One);
            }

            Aabb bounds = providers.Current.LocalBounds;
            while (providers.MoveNext())
            {
                bounds = bounds.Merge(providers.Current.LocalBounds);
            }

            return bounds.Size.LengthSquared() <= 0.0001f
                ? new Aabb(-Vector3.One * 0.5f, Vector3.One)
                : bounds;
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
        set => SetTransform(value, cascadeToChildren: true);
    }

    // cascadeToChildren must be false when re-applying the entity's own current transform just to
    // re-run SanitizeTransform (e.g. after a component set change) rather than a genuine move: a
    // snap in SelfRotation/SelfScale policy would otherwise compute a delta and drag every child by
    // it, with no undo entry recorded for those child moves.
    private void SetTransform(Transform3D value, bool cascadeToChildren)
    {
        Transform3D before = _transform;
        _transform = SanitizeTransform(value);
        if (Node != null)
        {
            Node.GlobalTransform = _transform;
        }

        if (!cascadeToChildren || _children.Count == 0 || before.IsEqualApprox(_transform))
        {
            return;
        }

        Transform3D delta = _transform * before.AffineInverse();
        foreach (SceneEntity child in _children.ToArray())
        {
            child.Transform = delta * child.Transform;
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
        _meshPickNodes.Clear();
    }

    /// <summary>
    /// An independent duplicate: fresh identity, no persisted record or parent link, and its own deep
    /// copy of every component. Used by copy/paste, which must not depend on the original entity still
    /// being loaded — once cloned, nothing here references the source, so streaming unloading (or even
    /// deleting) the original afterwards has no effect on the clone.
    /// </summary>
    public SceneEntity Clone()
    {
        var clone = new SceneEntity { Name = Name, Map = Map };
        foreach (SceneComponent component in Components)
        {
            clone.AddComponent(component.Clone());
        }

        clone.Transform = Transform;
        return clone;
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
        SetTransform(Transform, cascadeToChildren: false);
    }

    public bool RemoveComponent(SceneComponent component)
    {
        if (!_components.Remove(component))
        {
            return false;
        }

        component.Owner = null;
        RebuildRepresentation();
        SetTransform(Transform, cascadeToChildren: false);
        return true;
    }

    public void LoadComponent(SceneComponent component)
    {
        component.Owner = this;
        _components.Add(component);
    }

    public void RefreshRepresentation()
    {
        RebuildRepresentation();
        SetTransform(Transform, cascadeToChildren: false);
    }

    /// <summary>Builds the entity's viewport node. Called on the main thread.</summary>
    protected virtual Node3D BuildNode()
    {
        var root = new Node3D { Name = $"Entity{Id.Value}" };
        ClearPickNodes();
        foreach (ISceneNodeComponent component in Components.OfType<ISceneNodeComponent>())
        {
            if (component.BuildNode() is { } child)
            {
                root.AddChild(child);
                if (component is IMeshPickable)
                {
                    RegisterPickNode(child);
                }
            }
        }

        return root;
    }

    /// <summary>
    /// Declares a node whose <see cref="MeshInstance3D"/> descendants click selection should ray-test
    /// instead of settling for <see cref="LocalBounds"/>. <see cref="BuildNode"/> does this for every
    /// <see cref="IMeshPickable"/> component; an entity that builds its own node instead (a landscape
    /// chunk) calls it from its override.
    ///
    /// This matters most exactly where the bounds least resemble the geometry. A chunk's bounds are a
    /// nominal ±<see cref="LandscapeGrid.NominalHeightExtent"/> slab wrapped around a thin surface,
    /// and the camera normally sits inside one: unregistered, every click would land on terrain a few
    /// units away in mid-air, and nothing beyond it — no model, no marker — could ever be selected.
    /// </summary>
    protected void RegisterPickNode(Node3D node) => _meshPickNodes.Add(node);

    /// <summary>Forgets every registered pick node, for an override that is rebuilding its representation.</summary>
    protected void ClearPickNodes() => _meshPickNodes.Clear();

    /// <summary>
    /// Ray-vs-triangle test, in world space, against every <see cref="MeshInstance3D"/> under this
    /// entity's <see cref="IMeshPickable"/> components. Unlike the bounding-box test, a ray that passes
    /// through empty space inside the model's box but misses every face is a miss — that's the point of
    /// using it for click selection. <paramref name="t"/> is the distance along the ray, comparable
    /// with the distances the box test reports for other entities.
    /// </summary>
    /// <param name="hadGeometry">
    /// Whether there was actually anything to test: false for entities that only draw editor helpers,
    /// and for a model still streaming in. The caller must fall back to <see cref="LocalBounds"/> in
    /// that case — "nothing to test yet" must never read as "the click missed", which is precisely
    /// what makes an entity silently unselectable.
    /// </param>
    public bool TryPickGeometry(Vector3 origin, Vector3 dir, out float t, out bool hadGeometry)
    {
        t = float.PositiveInfinity;
        bool hit = false;
        bool geometry = false;
        foreach (Node3D node in _meshPickNodes)
        {
            if (GodotObject.IsInstanceValid(node))
            {
                hit |= TryPickNode(node, origin, dir, ref t, ref geometry);
            }
        }

        hadGeometry = geometry;
        return hit;
    }

    private static bool TryPickNode(Node3D node, Vector3 origin, Vector3 dir, ref float best, ref bool hadGeometry)
    {
        bool hit = false;
        if (node is MeshInstance3D { Mesh: { } mesh, Visible: true } && MeshPicking.HasTriangleSurface(mesh))
        {
            hadGeometry = true;

            Transform3D global = node.GlobalTransform;

            // Broad-phase in world space first: a complex model can have many mesh nodes, and most of
            // them are nowhere near the ray. This has to gate MeshPicking.Triangles() itself, not just
            // the triangle loop below it — that call is what populates the triangle cache, via a
            // marshaled SurfaceGetArrays per surface, and is the expensive part. Checking the box
            // afterwards (as this used to) meant the very first click anywhere on a heavy model paid
            // full extraction for every mesh in its entire tree, not just the handful the ray is near;
            // every click after was fast purely because the cache was already warm by then.
            if (MeshPicking.TryRayBox(origin, dir, global * mesh.GetAabb()))
            {
                Vector3[] triangles = MeshPicking.Triangles(mesh);

                // Test in mesh-local space so the triangles need no per-click transforming; the
                // unnormalised local direction keeps `best` a distance along the original world ray.
                Transform3D inv = global.AffineInverse();
                hit |= MeshPicking.TryRayTriangles(triangles, inv * origin, inv.Basis * dir, ref best);
            }
        }

        foreach (Node child in node.GetChildren())
        {
            if (child is Node3D child3D)
            {
                hit |= TryPickNode(child3D, origin, dir, ref best, ref hadGeometry);
            }
        }

        return hit;
    }

    /// <summary>Reacts to a change in selection state (e.g. highlight). Default does nothing.</summary>
    public virtual void OnSelectionChanged(bool selected) { }

    protected virtual Transform3D SanitizeTransform(Transform3D transform)
    {
        Basis rotation = transform.Basis.Orthonormalized();
        if (SelfRotation == SelfRotation.HeightOnly)
        {
            rotation = new Basis(Vector3.Up, rotation.GetEuler().Y);
        }
        else if (SelfRotation == SelfRotation.None)
        {
            rotation = Basis.Identity;
        }

        Vector3 scale = SelfScale switch
        {
            SelfScale.None => Vector3.One,
            SelfScale.Uniform => Vector3.One * (transform.Basis.Scale.X + transform.Basis.Scale.Y + transform.Basis.Scale.Z) / 3.0f,
            _ => transform.Basis.Scale,
        };

        transform.Basis = rotation.ScaledLocal(scale);
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
