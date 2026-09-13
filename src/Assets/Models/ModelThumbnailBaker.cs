using System;
using Godot;

namespace WorldMapStudio;

public enum ModelThumbnailBakeStatus
{
    Pending,
    Ready,
    Failed,
}

/// <summary>
/// Bakes one model at a time into a static thumbnail image, using a single shared offscreen viewport
/// rather than one live viewport per grid card - a grid of a few hundred cards would otherwise mean a
/// few hundred simultaneous render passes. A caller drives it with <see cref="Begin"/> then polls
/// <see cref="Poll"/> once per frame until it settles, mirroring <see cref="ModelPreviewRenderer"/>'s
/// setup (own world, camera, light) but rendering once and reading the pixels back instead of staying live.
/// </summary>
public sealed class ModelThumbnailBaker : IDisposable
{
    public const int Resolution = 128;

    /// <summary>Frames to let a freshly-instantiated model sit before capturing it. References (e.g. WMO
    /// doodads) resolve asynchronously over a few frames; this is a fixed budget rather than tracking
    /// completion, so a heavy WMO still produces a thumbnail even if some doodads land late (spike scope
    /// - see model-browser-grid-plan.md's "Settling rule").</summary>
    private const int SettleFrames = 3;

    private enum Phase
    {
        LoadingModel,
        Settling,
        WaitingForRender,
        Done,
    }

    private readonly AssetSystem _assets;
    private readonly MeshMaterialSystem _materials;
    private readonly SubViewport _viewport;
    private readonly Camera3D _camera;
    private readonly DirectionalLight3D _light;

    private System.Threading.Tasks.Task<ModelAsset?>? _load;
    private Node3D? _modelNode;
    private Phase _phase;
    private int _settleFrame;

    public ModelThumbnailBaker(AssetSystem assets, MeshMaterialSystem materials, Node owner)
    {
        _assets = assets;
        _materials = materials;
        _viewport = new SubViewport
        {
            Name = "ModelThumbnailViewport",
            Size = new Vector2I(Resolution, Resolution),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            OwnWorld3D = true,
        };

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.18f, 0.18f, 0.18f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.45f, 0.45f, 0.45f),
        };

        _camera = new Camera3D
        {
            Name = "ThumbnailCamera",
            Current = true,
            Near = 0.01f,
            Far = 10000.0f,
            Environment = environment,
        };
        _light = new DirectionalLight3D
        {
            Name = "ThumbnailLight",
            LightEnergy = 1.4f,
            RotationDegrees = new Vector3(-45.0f, 35.0f, 0.0f),
        };

        _viewport.AddChild(_camera);
        _viewport.AddChild(_light);
        owner.AddChild(_viewport);
    }

    public bool IsBusy => _load != null;

    /// <summary>Starts baking <paramref name="path"/>. Must not be called while <see cref="IsBusy"/>.</summary>
    public void Begin(string path)
    {
        _load = _assets.LoadModelAssetAsync(path);
        _phase = Phase.LoadingModel;
        _settleFrame = 0;
    }

    /// <summary>Advances the current bake by one frame. Call once per frame while <see cref="IsBusy"/>.</summary>
    public ModelThumbnailBakeStatus Poll()
    {
        switch (_phase)
        {
            case Phase.LoadingModel:
                if (_load!.IsFaulted)
                {
                    return EndJob(ModelThumbnailBakeStatus.Failed);
                }

                if (!_load.IsCompletedSuccessfully)
                {
                    return ModelThumbnailBakeStatus.Pending;
                }

                if (_load.Result == null)
                {
                    return EndJob(ModelThumbnailBakeStatus.Failed);
                }

                ShowModel(_load.Result);
                _phase = Phase.Settling;
                return ModelThumbnailBakeStatus.Pending;

            case Phase.Settling:
                if (++_settleFrame < SettleFrames)
                {
                    return ModelThumbnailBakeStatus.Pending;
                }

                _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
                _phase = Phase.WaitingForRender;
                return ModelThumbnailBakeStatus.Pending;

            case Phase.WaitingForRender:
                // UpdateMode.Once renders on the viewport's next internal update, which lands sometime
                // during this same engine frame - wait one more Poll so the pixels are actually there
                // before GetImage() reads them back.
                _phase = Phase.Done;
                return ModelThumbnailBakeStatus.Pending;

            case Phase.Done:
                return ModelThumbnailBakeStatus.Ready;

            default:
                throw new InvalidOperationException($"Unhandled thumbnail bake phase {_phase}.");
        }
    }

    /// <summary>Reads back the rendered frame and ends the job. Only valid right after <see cref="Poll"/>
    /// returns <see cref="ModelThumbnailBakeStatus.Ready"/>.</summary>
    public ImageTexture TakeResult()
    {
        Image image = _viewport.GetTexture().GetImage();
        ImageTexture texture = ImageTexture.CreateFromImage(image);
        EndJob(ModelThumbnailBakeStatus.Ready);
        return texture;
    }

    public void Dispose()
    {
        ClearModel();
        _viewport.QueueFree();
    }

    private ModelThumbnailBakeStatus EndJob(ModelThumbnailBakeStatus status)
    {
        ClearModel();
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        _load = null;
        return status;
    }

    private void ShowModel(ModelAsset model)
    {
        _modelNode = model.Instantiate(_materials);
        _viewport.AddChild(_modelNode);
        ModelCameraFraming.Frame(_camera, model.LocalBounds);
    }

    private void ClearModel()
    {
        if (_modelNode != null)
        {
            _modelNode.QueueFree();
            _modelNode = null;
        }
    }
}
