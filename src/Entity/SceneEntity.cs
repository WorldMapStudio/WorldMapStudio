using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// An entity placed in a map: it has a world transform and bounds, and owns its Godot
/// representation in the viewport. Streamed in and out as the user moves around the map. Knows nothing
/// about which table stores it — <see cref="MapSceneEntity"/> is the editor-authored kind, and a
/// plugin derives its own for entities stored elsewhere.
/// </summary>
public abstract class SceneEntity : Entity
{
    private Transform3D _transform = Transform3D.Identity;

    // WorldBounds is Transform * LocalBounds, an eight-corner transform over a component scan, and the
    // terrain raycast reads it for every loaded chunk twice a frame. It only moves when the transform
    // or the component set does; every content change that alters LocalBounds routes through
    // RefreshRepresentation, which re-applies the transform. So a counter bumped in the Transform setter and
    // on a component being loaded covers every case, and the box is computed once per change.
    private Aabb _worldBounds;
    private int _worldBoundsVersion = -1;
    private int _boundsVersion;

    private readonly List<SceneComponent> _components = [];
    private readonly List<Node3D> _meshPickNodes = [];

    /// <summary>The representation node while loaded into a viewport, otherwise null.</summary>
    protected Node3D? Node { get; private set; }

    /// <summary>The editor map this instance is loaded into, which is what streaming scopes by. On a
    /// <see cref="MapSceneEntity"/> it is also the persisted owner; on an entity stored elsewhere it is
    /// only the map it was scanned for.</summary>
    public MapId Map { get; set; } = new(0);

    /// <summary>Primary key of this entity's row in the editor's entity table; null until it first has
    /// editor-side data committed.</summary>
    public int? RecordId { get; set; }

    [ScriptProperty(Mutable = true)]
    public string Name { get; set; } = "Entity";

    /// <summary>The tags this entity carries. Changed only through <see cref="SceneEntityRegistry.SetTags"/>,
    /// which is what tells caches keyed on tags to refresh; loading sets it directly, before the entity
    /// is added to the registry.</summary>
    public EntityTagSet Tags { get; internal set; }

    /// <summary>The tags as last loaded or committed — what staging diffs <see cref="Tags"/> against, so
    /// a commit never reads the table to find out what changed.</summary>
    public EntityTagSet PersistedTags { get; internal set; }

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
    public Aabb WorldBounds
    {
        get
        {
            if (_worldBoundsVersion != _boundsVersion)
            {
                _worldBounds = _transform * LocalBounds;
                _worldBoundsVersion = _boundsVersion;
            }

            return _worldBounds;
        }
    }

    public bool IsRepresented => Node != null;

    private bool _visible = true;

    /// <summary>
    /// Whether the view filter (<see cref="ViewCategorySystem"/>) wants this entity drawn and
    /// pickable. A mirror of <see cref="Transform"/>'s own shape: the field is the truth, the node's
    /// <see cref="Node3D.Visible"/> is a write-only reflection of it, and <see cref="CreateRepresentation"/>
    /// applies whatever this already holds — so hiding a represented entity never destroys or rebuilds
    /// its node, and hiding an unrepresented one costs nothing at all.
    /// </summary>
    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value)
            {
                return;
            }

            _visible = value;
            if (Node != null)
            {
                Node.Visible = value;
            }
        }
    }

    public IReadOnlyList<SceneComponent> Components => _components;

    public override string DisplayName => Name;

    /// <summary>
    /// The entity's extent for deciding which chunks it occupies: <see cref="EffectiveLocalBounds"/>
    /// with every component whose <see cref="ISceneBoundsProvider.ContributesChunkOwnership"/> is false
    /// left out. Null when the entity claims nowhere in particular — an entity carrying only a global
    /// light, say.
    ///
    /// Separate from <see cref="LocalBounds"/> because the two answer different questions. Streaming
    /// asks "should this be loaded here", and a global light must answer yes everywhere; chunk
    /// tracking asks "is this what makes that chunk exist", and a global light must answer no
    /// anywhere. Merging both into one box makes a light look like terrain covering the entire map.
    /// </summary>
    public Aabb? LocalChunkBounds
    {
        get
        {
            List<ISceneBoundsProvider> providers = Components.OfType<ISceneBoundsProvider>().ToList();
            if (providers.Count == 0)
            {
                // No provider at all is the same "somewhere, unit sized" fallback EffectiveLocalBounds
                // uses — an entity with nothing to size it still sits at a place.
                return EffectiveLocalBounds;
            }

            Aabb? bounds = null;
            foreach (ISceneBoundsProvider provider in providers.Where(provider => provider.ContributesChunkOwnership))
            {
                bounds = bounds is { } merged ? merged.Merge(provider.LocalBounds) : provider.LocalBounds;
            }

            return bounds;
        }
    }

    /// <summary><see cref="LocalChunkBounds"/> placed by <see cref="Transform"/> — the world extent
    /// chunk ownership is tested against.</summary>
    public Aabb? WorldChunkBounds => LocalChunkBounds is { } bounds ? Transform * bounds : null;

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
    /// The field is the truth and the node is a mirror of it, never read back. Reading it from the node would make
    /// this getter a Godot node access — and background chunk building reads deformer transforms, so a rebuild racing
    /// a gizmo drag would touch a live node off the main thread. Nothing writes the node's transform except the two
    /// places below, so the mirror cannot drift.
    /// </summary>
    public Transform3D Transform
    {
        get => _transform;
        set
        {
            _boundsVersion++;
            _transform = SanitizeTransform(value);
            if (Node != null)
            {
                Node.GlobalTransform = _transform;
            }
        }
    }

    // Re-applies the current transform so SanitizeTransform and the bounds cache see a changed component set.
    private void Resanitize() => Transform = _transform;

    public void CreateRepresentation(Node parent)
    {
        if (Node != null)
        {
            return;
        }

        Node = BuildNode();
        parent.AddChild(Node);
        Node.GlobalTransform = _transform;
        Node.Visible = _visible;
    }

    /// <summary>
    /// The entity has left the registry for good. Anything it owns outright that the garbage collector
    /// would otherwise leave to a finalizer — a Godot resource, most of all — is released here instead.
    /// That distinction matters: <see cref="DestroyRepresentation"/> also runs when an entity merely
    /// turns peripheral and will be drawn again later, so it is the wrong place to free what a rebuilt
    /// representation would need back.
    /// </summary>
    public virtual void Unload()
    {
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
    /// An independent duplicate: fresh identity, no persisted record, and its own deep copy of everything
    /// the editor owns on it. Null for entity kinds that can't be duplicated. Used by copy/paste and
    /// prefabs, which must not depend on the original entity still being loaded — once cloned, nothing
    /// here references the source, so streaming unloading (or even deleting) the original afterwards
    /// has no effect on the clone.
    /// </summary>
    public virtual SceneEntity? Clone() => null;

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
        Resanitize();
    }

    public bool RemoveComponent(SceneComponent component)
    {
        if (!_components.Remove(component))
        {
            return false;
        }

        component.Owner = null;
        RebuildRepresentation();
        Resanitize();
        return true;
    }

    public void LoadComponent(SceneComponent component)
    {
        component.Owner = this;
        _components.Add(component);
        _boundsVersion++;
    }

    public void RefreshRepresentation()
    {
        RebuildRepresentation();
        Resanitize();
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
            // afterwards would make the very first click anywhere on a heavy model pay full extraction
            // for every mesh in its entire tree, not just the handful the ray is near.
            if (MeshPicking.TryRayBox(origin, dir, global * mesh.GetAabb()))
            {
                Vector3[] triangles = MeshPicking.Triangles(mesh);

                // Test in mesh-local space so the triangles need no per-click transforming; the
                // unnormalised local direction keeps `best` a distance along the original world ray.
                Transform3D inv = global.AffineInverse();
                hit |= MeshPicking.TryRayTriangles(triangles, inv * origin, inv.Basis * dir, ref best);
            }
        }

        // Indexed rather than GetChildren(): that returns a Godot array whose enumeration marshals a
        // Variant per element and whose own handle is left to the finalizer, per node of every tree
        // this walks.
        int children = node.GetChildCount();
        for (int i = 0; i < children; i++)
        {
            if (node.GetChild(i) is Node3D child3D)
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
