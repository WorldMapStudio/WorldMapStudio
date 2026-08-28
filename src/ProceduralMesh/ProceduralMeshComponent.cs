using System;
using Godot;

namespace WorldMapStudio;

public sealed class ProceduralMeshComponent : SceneComponent, ISceneBoundsProvider, ISceneNodeComponent
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

    public ProceduralMeshNetwork Network { get; private set; } = new();

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

    public void ReplaceNetwork(ProceduralMeshNetwork network)
    {
        Network = network.Clone();
        Invalidate();
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
                MaterialOverride = BuildMaterial(surface),
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

    private StandardMaterial3D BuildMaterial(ProceduralMeshSurface surface)
    {
        Texture2D? texture = surface.TexturePath.Length > 0 ? _system.Context.Assets.LoadTextureAsset(surface.TexturePath) : null;
        return new StandardMaterial3D
        {
            AlbedoTexture = texture,
            AlbedoColor = texture == null ? surface.AlbedoColor : surface.AlbedoColor,
            Roughness = 0.9f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        };
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
