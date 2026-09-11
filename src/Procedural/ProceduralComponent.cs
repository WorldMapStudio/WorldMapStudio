using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Places a <see cref="ProceduralModel"/> in the scene. The component owns no authored data itself —
/// function, parameters, network and all — only which model it references, so many placements can
/// share one model and editing it from any of them (or a window, or a script) updates every placement.
/// See <see cref="ProceduralSystem.Update"/> for how a placement notices the model changed under it.
///
/// Also a <see cref="ILandscapeDeformer"/>: a bound model may paint landscape channels as well as (or
/// instead of) rendering geometry. <see cref="ILandscapeDeformer.Rasterize"/> and
/// <see cref="ILandscapeDeformer.InfluenceBounds"/> may run on a background chunk-build worker, so they
/// read only the <see cref="ProceduralBuildResult"/> published by the last <see cref="BuildNode"/> —
/// never <see cref="Model"/> or <see cref="Network"/> live, which would race the catalog the way
/// <see cref="LandscapeBuilder"/>'s class comment warns against for live scene state generally.
/// </summary>
public sealed class ProceduralComponent : SceneComponent, ISceneBoundsProvider, ISceneNodeComponent, INetworkEditable, ILandscapeDeformer, IPreparableLandscapeDeformer, ITransformPolicy, IMeshPickable
{
    /// <summary>The single source of truth for this component kind's id — <see cref="ProceduralComponentType"/>
    /// and <see cref="ProceduralComponentPersistence"/> both reference this instead of restating it.</summary>
    public const string Kind = "procedural-mesh";

    private static readonly VertexNetwork EmptyNetwork = new();

    private readonly ProceduralSystem _system;
    private int? _modelId;

    // Set by the scan that resolved this component's model, for an entity outside the live scene — an
    // offline ScanSceneAsync result, a landscape build's placement list, an export run. Never read
    // while the registry already has an answer; see Model.
    private ProceduralModel? _attached;

    // What the representation was last built from. Compared by ProceduralSystem.Update against
    // the live (ModelId, Model.Revision) pair every frame, so a model edited from a different entity
    // (or a window, or a script) still rebuilds this placement.
    private int? _representedModelId;
    private int _representedRevision = -1;

    // Published by BuildNode, which always runs on the main thread — the only state Rasterize and
    // InfluenceBounds are allowed to read. _publishedBounds is the paint's own footprint only (zero
    // while paintless), not the model's rendered bounds — geometry has no bearing on what terrain a
    // placement touches.
    private ProceduralPaint _publishedPaint = ProceduralPaint.Empty;
    private Aabb _publishedBounds;

    public ProceduralComponent(ProceduralSystem system)
    {
        // Guarded because a null here is otherwise invisible until the component is first built, and
        // then surfaces as a per-frame NullReferenceException from deep inside the viewport's draw
        // loop with no hint that the real mistake was made back at construction time.
        ArgumentNullException.ThrowIfNull(system);
        _system = system;
    }

    public int? ModelId
    {
        get => _modelId;
        set
        {
            if (_modelId == value)
            {
                return;
            }

            _modelId = value;

            // Guarded on Owner rather than firing unconditionally: a persister sets ModelId via this
            // same setter while constructing a component for a scan, before the component is ever
            // attached to an entity (see ProceduralComponentPersistence.LoadAsync) — at that instant the
            // model may be sitting unresolved for a few more lines pending AttachModel, or (for an
            // offline scan) may never be meant to touch the live registry at all. Requesting here would
            // needlessly re-query the one case, and wrongly publish into the live registry for the
            // other. Once the component is owned, this covers a script or inspector rebind naming an id
            // outside a scan — ProceduralSystem.Update's dangling check is the remaining safety net for
            // everything else (e.g. an undone delete that reuses the component instance without ever
            // calling this setter).
            if (value is int id && Owner != null)
            {
                _system.RequestLoad(id);
            }

            Owner?.RefreshRepresentation();
        }
    }

    /// <summary>
    /// The bound model, or null if <see cref="ModelId"/> is unset or dangling. The registry is checked
    /// first, so a live placement always resolves to the same instance every other placement, window,
    /// undo or script edits — the attachment (see <see cref="AttachModel"/>) is only ever the answer for
    /// an entity that is not in the live scene.
    /// </summary>
    public ProceduralModel? Model => _system.FindModel(_modelId) ?? _attached;

    /// <summary>
    /// Sets the model an offline scan resolved for this component — see <see cref="SceneEntityScanCatalog"/>.
    /// The instance is owned by whatever scanned it and is never written to; a live placement never
    /// reads it, since <see cref="Model"/> checks the registry first.
    /// </summary>
    public void AttachModel(ProceduralModel model) => _attached = model;

    /// <summary>
    /// Called on every entity <see cref="StreamingSystem.Reconcile"/> or <see cref="PrefabSystem.LoadLibrary"/>
    /// adds to the live scene, so a model the registry has since evicted can never be resurrected by a
    /// stale attachment left over from the scan that loaded this placement.
    /// </summary>
    public void ClearAttachment() => _attached = null;

    /// <summary>The bound model's network, or an empty one while unbound.</summary>
    public VertexNetwork Network => Model?.Network ?? EmptyNetwork;

    /// <summary>The bound model's function, or null while unbound or dangling. What
    /// <see cref="PlanarXZ"/> and the <see cref="ITransformPolicy"/> members forward to — switching a
    /// model's function can change what a placement's transform means (see
    /// <c>.godot/ProceduralOutputsPlan.md</c> §3), which is a real, deliberate consequence rather than
    /// an oversight.</summary>
    private IProceduralFunction? BoundFunction => Model is { } model ? _system.Find(model.FunctionId) : null;

    public bool PlanarXZ => BoundFunction?.PlanarNetwork ?? false;

    public SelfRotation SelfRotation => BoundFunction?.SelfRotation ?? SelfRotation.Full;

    public SelfScale SelfScale => BoundFunction?.SelfScale ?? SelfScale.PerAxis;

    public bool SnapToTerrainOnPlace => BoundFunction?.SnapToTerrainOnPlace ?? false;

    public Vector3 SnapVertex(Vector3 worldPosition) => BoundFunction?.SnapVertex(worldPosition) ?? worldPosition;

    public override string TypeId => Kind;

    public override string DisplayName => "Procedural Mesh";

    public Aabb LocalBounds
    {
        get
        {
            ProceduralBuildResult result = BuildOutput();
            if (result.Models.Count == 0 && result.Paint.Strokes.Count == 0)
            {
                return Network.Bounds();
            }

            return result.LocalBounds;
        }
    }

    public override int ContentVersion => HashCode.Combine(ModelId, Model?.ContentVersion ?? 0);

    /// <summary>The bound model's last-published landscape contribution, for problem reporting
    /// (missing channels) and for <see cref="Rasterize"/>. Empty while unbound or unrepresented.</summary>
    public ProceduralPaint Paint => _publishedPaint;

    /// <summary>Stable identity for this placement's claim groups. Built from the entity's persistent
    /// id (or its in-session id before a first save), never from scan order.</summary>
    public string DeformerKey => Entity.RecordId is { } id
        ? $"entity:{id}:procedural"
        : $"entity:new:{Entity.Id.Value}:procedural";

    /// <summary>The published paint footprint, transformed — zero-size while unbound, unrepresented, or
    /// bound to a model that paints nothing, so <see cref="LandscapeBuilder"/> and
    /// <see cref="LandscapeDirtyTracker"/> can skip this placement entirely rather than treating a
    /// degenerate box as touching every chunk. A model's rendered geometry has no bearing on this — only
    /// what it paints does.</summary>
    public Aabb InfluenceBounds => Entity.Transform * _publishedBounds;

    /// <summary>A procedural model never claims a layer itself — which layer and material its painted
    /// channels end up feeding is <see cref="LandscapeMaterialBindComponent"/>'s job, kept separate so
    /// a placement never hardcodes a mapping that belongs to the project's catalog.</summary>
    public IEnumerable<LandscapeClaimGroup> Claim(in LandscapeClaimContext context) => [];

    public void Rasterize(in LandscapeRasterContext context) =>
        ProceduralPaintRasterizer.Rasterize(_publishedPaint, Entity.Transform, context);

    /// <summary>Publishes what the bound model paints, which is what <see cref="Rasterize"/> and
    /// <see cref="InfluenceBounds"/> read. Separate from <see cref="BuildNode"/> because a deformer has
    /// to contribute whether or not anything is drawing it — an offline build has no viewport at all,
    /// and a live placement streamed in but not yet represented deforms nothing until it gets one.</summary>
    public void PublishLandscapeContribution()
    {
        ProceduralBuildResult result = BuildOutput();
        _publishedPaint = result.Paint;
        _publishedBounds = PaintBounds(result.Paint);
    }

    // Main-thread-bound: BuildOutput reaches ProceduralSystem.Build, which resolves assets and mesh
    // materials off live catalog state the editor mutates.
    public void Request(LandscapeBuildRequest request) => request.RequiresMainThread();

    public void Prepare(LandscapeBuildResources resources) => PublishLandscapeContribution();

    /// <summary>Whether the bound model has moved on since this placement's representation was last built.</summary>
    public bool NeedsRefresh => _representedModelId != ModelId || _representedRevision != (Model?.Revision ?? -1);

    /// <summary>What a network edit pins and persists against — the bound model, not this placement,
    /// since the network lives there and may be shared.</summary>
    public IEntity EditTarget => Model ?? throw new InvalidOperationException("Procedural mesh has no bound model.");

    /// <summary>Every loaded placement referencing the same model.</summary>
    public IEnumerable<SceneEntity> AffectedEntities => _modelId is int id
        ? _system.Context.Scene.Entities.Where(entity => entity.Component<ProceduralComponent>()?.ModelId == id)
        : Owner is { } owner ? [owner] : [];

    /// <summary>Replaces the bound model's network wholesale. A no-op while unbound.</summary>
    public void ReplaceNetwork(VertexNetwork network)
    {
        Model?.ReplaceNetwork(network);
        Owner?.RefreshRepresentation();
    }

    /// <summary>An independent placement of the same model — cloning a component shares its bound
    /// model rather than forking the geometry, the same way cloning a <see cref="ModelRendererComponent"/>
    /// shares the referenced asset path.</summary>
    public override SceneComponent Clone() => new ProceduralComponent(_system) { ModelId = ModelId };

    public Node3D BuildNode()
    {
        _representedModelId = ModelId;
        _representedRevision = Model?.Revision ?? -1;

        var root = new Node3D { Name = "ProceduralComponent" };

        PublishLandscapeContribution();

        // A second BuildOutput is free: ProceduralSystem.Build is cached on the model's identity and
        // revision, so this hits the same result PublishLandscapeContribution just built from.
        ProceduralBuildResult result = BuildOutput();

        foreach (ProceduralModelOutput output in result.Models)
        {
            Node3D node = output.Asset.Instantiate(_system.Context.MeshMaterials);
            node.Name = output.Slot.DisplayName;
            node.Transform = output.Transform;
            root.AddChild(node);
        }

        return root;
    }

    private ProceduralBuildResult BuildOutput() => Model is { } model ? _system.Build(model) : ProceduralBuildResult.Empty;

    /// <summary>The paint's own flat bounds, grown to the landscape's nominal height extent — a
    /// paint-only placement (a road) sits at Y = 0 but paints terrain at any height. Zero-size when
    /// there is nothing to paint.</summary>
    private static Aabb PaintBounds(ProceduralPaint paint)
    {
        if (paint.Strokes.Count == 0)
        {
            return new Aabb();
        }

        Aabb flat = paint.LocalBounds;
        return new Aabb(
            new Vector3(flat.Position.X, -LandscapeGrid.NominalHeightExtent, flat.Position.Z),
            new Vector3(flat.Size.X, LandscapeGrid.NominalHeightExtent * 2.0f, flat.Size.Z));
    }
}
