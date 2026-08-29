using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Places a <see cref="ProceduralModel"/> in the scene. The component owns no authored data itself —
/// function, parameters, network and all — only which model it references, so many placements can
/// share one model and editing it from any of them (or a window, or a script) updates every placement.
/// See <see cref="ProceduralMeshSystem.Update"/> for how a placement notices the model changed under it.
/// </summary>
public sealed class ProceduralMeshComponent : SceneComponent, ISceneBoundsProvider, ISceneNodeComponent, INetworkEditable
{
    private static readonly VertexNetwork EmptyNetwork = new();

    private readonly ProceduralMeshSystem _system;
    private int? _modelId;

    // What the representation was last built from. Compared by ProceduralMeshSystem.Update against
    // the live (ModelId, Model.Revision) pair every frame, so a model edited from a different entity
    // (or a window, or a script) still rebuilds this placement.
    private int? _representedModelId;
    private int _representedRevision = -1;

    public ProceduralMeshComponent(ProceduralMeshSystem system)
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
            Owner?.RefreshRepresentation();
        }
    }

    /// <summary>The bound model, or null if <see cref="ModelId"/> is unset or dangling.</summary>
    public ProceduralModel? Model => _system.FindModel(_modelId);

    /// <summary>The bound model's network, or an empty one while unbound.</summary>
    public VertexNetwork Network => Model?.Network ?? EmptyNetwork;

    public bool PlanarXZ => false;

    public override string TypeId => "procedural-mesh";

    public override string DisplayName => "Procedural Mesh";

    public Aabb LocalBounds
    {
        get
        {
            ModelAsset output = BuildOutput();
            if (output.Surfaces.Count > 0)
            {
                return output.LocalBounds;
            }

            return Network.Bounds();
        }
    }

    public override int ContentVersion => HashCode.Combine(ModelId, Model?.ContentVersion ?? 0);

    /// <summary>Whether the bound model has moved on since this placement's representation was last built.</summary>
    public bool NeedsRefresh => _representedModelId != ModelId || _representedRevision != (Model?.Revision ?? -1);

    /// <summary>What a network edit pins and persists against — the bound model, not this placement,
    /// since the network lives there and may be shared.</summary>
    public IEntity EditTarget => Model ?? throw new InvalidOperationException("Procedural mesh has no bound model.");

    /// <summary>Every loaded placement referencing the same model.</summary>
    public IEnumerable<SceneEntity> AffectedEntities => _modelId is int id
        ? _system.Context.Scene.Entities.Where(entity => entity.Component<ProceduralMeshComponent>()?.ModelId == id)
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
    public override SceneComponent Clone() => new ProceduralMeshComponent(_system) { ModelId = ModelId };

    public Node3D BuildNode()
    {
        _representedModelId = ModelId;
        _representedRevision = Model?.Revision ?? -1;

        ModelAsset output = BuildOutput();
        if (output.Surfaces.Count == 0)
        {
            var placeholderRoot = new Node3D { Name = "ProceduralMeshComponent" };
            placeholderRoot.AddChild(Placeholder());
            return placeholderRoot;
        }

        Node3D node = output.Instantiate(_system.Context.MeshMaterials);
        node.Name = "ProceduralMeshComponent";
        return node;
    }

    private ModelAsset BuildOutput() => Model is { } model ? _system.Build(model) : ProceduralMeshSystem.EmptyOutput;

    private static MeshInstance3D Placeholder() => new()
    {
        Name = "MissingProceduralMesh",
        Mesh = new BoxMesh { Size = Vector3.One },
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.72f, 0.42f, 0.85f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        },
    };
}
