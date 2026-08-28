using System;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

public sealed class ModelRendererComponent : SceneComponent, ISceneBoundsProvider, ISceneNodeComponent
{
    private readonly AssetSystem _assets;
    private string _modelPath = "";
    private string _pendingPath = "";

    public ModelRendererComponent(AssetSystem assets)
    {
        _assets = assets;
    }

    public string ModelPath
    {
        get => _modelPath;
        set
        {
            if (_modelPath == value)
            {
                return;
            }

            _modelPath = value;
            Owner?.RefreshRepresentation();
        }
    }

    public override string TypeId => "model-renderer";

    public override string DisplayName => "Model Renderer";

    public override int ContentVersion => HashCode.Combine(ModelPath);

    public Aabb LocalBounds => TryGetModel(out ModelAsset? model)
        ? Centered(model!.LocalBounds)
        : new Aabb(-Vector3.One * 0.5f, Vector3.One);

    public Node3D BuildNode()
    {
        if (!TryGetModel(out ModelAsset? model))
        {
            return Placeholder();
        }

        Node3D node = model!.Instantiate(_assets);
        node.Name = "ModelRendererComponent";
        node.Position = -model.LocalBounds.GetCenter();
        return node;
    }

    private bool TryGetModel(out ModelAsset? model)
    {
        model = null;
        if (_modelPath.Length == 0)
        {
            return false;
        }

        Task<ModelAsset?> task = _assets.LoadModelAssetAsync(_modelPath);
        if (task.IsCompletedSuccessfully)
        {
            model = task.Result;
            return model != null;
        }

        if (_pendingPath != _modelPath)
        {
            _pendingPath = _modelPath;
            string path = _modelPath;
            WorkQueue.Schedule("Refresh Model Renderer", async work =>
            {
                await task.ConfigureAwait(false);
                await work.SwitchToMain();
                if (_modelPath == path)
                {
                    Owner?.RefreshRepresentation();
                }
            });
        }

        return false;
    }

    private static Aabb Centered(Aabb bounds) => new(bounds.Size * -0.5f, bounds.Size);

    private static Node3D Placeholder()
    {
        var node = new Node3D { Name = "ModelRendererComponent" };
        node.AddChild(new MeshInstance3D
        {
            Name = "MissingModel",
            Mesh = new BoxMesh { Size = Vector3.One },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.75f, 0.2f, 0.35f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        });
        return node;
    }
}
