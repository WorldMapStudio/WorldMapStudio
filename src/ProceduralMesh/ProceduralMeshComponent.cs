using System;
using Godot;

namespace WorldMapStudio;

public sealed class ProceduralMeshComponent : SceneComponent, ISceneBoundsProvider, ISceneNodeComponent, INetworkEditable
{
    private readonly ProceduralMeshSystem _system;
    private ProceduralMeshOutput? _cached;
    private string _cacheKey = "";
    private string _functionId = "builtin.mesh.tube_network";
    private string _parameters = "";

    public ProceduralMeshComponent(ProceduralMeshSystem system)
    {
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

    public VertexNetwork Network { get; private set; } = new();

    public bool PlanarXZ => false;

    public override string TypeId => "procedural-mesh";

    public override string DisplayName => "Procedural Mesh";

    public Aabb LocalBounds
    {
        get
        {
            ProceduralMeshOutput output = BuildOutput();
            if (output.Surfaces.Count > 0)
            {
                return output.LocalBounds;
            }

            return Network.Bounds();
        }
    }

    public override int ContentVersion =>
        HashCode.Combine(FunctionId, Parameters, _system.Find(FunctionId)?.Version ?? 0, Network.Fingerprint());

    public void ReplaceNetwork(VertexNetwork network)
    {
        Network = network.Clone();
        Invalidate();
    }

    public override SceneComponent Clone()
    {
        var clone = new ProceduralMeshComponent(_system) { FunctionId = FunctionId, Parameters = Parameters };
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
        var root = new Node3D { Name = "ProceduralMeshComponent" };
        ProceduralMeshOutput output = BuildOutput();
        if (output.Surfaces.Count == 0)
        {
            root.AddChild(Placeholder());
            return root;
        }

        for (int i = 0; i < output.Surfaces.Count; i++)
        {
            ProceduralMeshSurface surface = output.Surfaces[i];
            root.AddChild(new MeshInstance3D
            {
                Name = surface.Name.Length == 0 ? $"Surface{i}" : surface.Name,
                Mesh = surface.Mesh,
                MaterialOverride = ModelMaterialFactory.Build(_system.Context.Assets, surface.Material),
            });
        }

        return root;
    }

    private ProceduralMeshOutput BuildOutput()
    {
        string key = $"{FunctionId}|{Parameters}|{_system.Find(FunctionId)?.Version ?? 0}|{Network.Fingerprint()}";
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
