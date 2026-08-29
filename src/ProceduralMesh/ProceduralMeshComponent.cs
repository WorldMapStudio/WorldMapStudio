using System;
using Godot;

namespace WorldMapStudio;

public sealed class ProceduralMeshComponent : SceneComponent, ISceneBoundsProvider, ISceneNodeComponent, INetworkEditable
{
    private readonly ProceduralMeshSystem _system;
    private ModelAsset? _cached;
    private string _cacheKey = "";
    private string _functionId = "builtin.mesh.tube_network";
    private string _parameters = "";
    private string _formatId = "";
    private string _materials = "";

    public ProceduralMeshComponent(ProceduralMeshSystem system)
    {
        // Guarded because a null here is otherwise invisible until the component is first built, and
        // then surfaces as a per-frame NullReferenceException from deep inside the viewport's draw
        // loop with no hint that the real mistake was made back at construction time.
        ArgumentNullException.ThrowIfNull(system);
        _system = system;
    }

    public string FunctionId
    {
        get => _functionId;
        set
        {
            if (_functionId == value)
            {
                return;
            }

            _functionId = value;
            Invalidate();
        }
    }

    public string Parameters
    {
        get => _parameters;
        set
        {
            if (_parameters == value)
            {
                return;
            }

            _parameters = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Which <see cref="IModelFormat"/> this procedural mesh authors, e.g. "wow.format.wmo" for a
    /// plugin-defined format. Empty defers to the bound function's first supported format, or the
    /// plain authorable mesh format if the function does not care.
    /// </summary>
    public string FormatId
    {
        get => _formatId;
        set
        {
            if (_formatId == value)
            {
                return;
            }

            _formatId = value;
            Invalidate();
        }
    }

    /// <summary>Serialized <see cref="ProceduralMeshMaterialBindings"/> for the bound function's material slots.</summary>
    public string Materials
    {
        get => _materials;
        set
        {
            if (_materials == value)
            {
                return;
            }

            _materials = value;
            Invalidate();
        }
    }

    public VertexNetwork Network { get; private set; } = new();

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

    public override int ContentVersion => HashCode.Combine(
        FunctionId, Parameters, FormatId, Materials,
        _system.Find(FunctionId)?.Version ?? 0, Network.Fingerprint(), _system.Context.MeshMaterials.PresetContentVersion);

    public void ReplaceNetwork(VertexNetwork network)
    {
        Network = network.Clone();
        Invalidate();
    }

    public override SceneComponent Clone()
    {
        var clone = new ProceduralMeshComponent(_system) { FunctionId = FunctionId, Parameters = Parameters, FormatId = FormatId, Materials = Materials };
        clone.ReplaceNetwork(Network);
        return clone;
    }

    public void Invalidate()
    {
        _cacheKey = "";
        Owner?.RefreshRepresentation();
    }

    public Node3D BuildNode()
    {
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

    private ModelAsset BuildOutput()
    {
        string key = $"{FunctionId}|{Parameters}|{FormatId}|{Materials}|{_system.Find(FunctionId)?.Version ?? 0}|{Network.Fingerprint()}|{_system.Context.MeshMaterials.PresetContentVersion}";
        if (_cached != null && _cacheKey == key)
        {
            return _cached;
        }

        _cached = _system.Build(this);
        _cacheKey = key;
        return _cached;
    }

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
