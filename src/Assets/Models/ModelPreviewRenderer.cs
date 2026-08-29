using System;
using System.Threading.Tasks;
using Godot;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

public sealed class ModelPreviewRenderer : IDisposable
{
    private readonly AssetSystem _assets;
    private readonly MeshMaterialSystem _materials;
    private readonly Node _owner;
    private readonly SubViewport _viewport;
    private readonly Camera3D _camera;
    private readonly DirectionalLight3D _light;
    private Node3D? _modelNode;
    private Task<ModelAsset?>? _load;
    private string _path = "";
    private string _loadedPath = "";

    public ModelPreviewRenderer(AssetSystem assets, MeshMaterialSystem materials, Node owner)
    {
        _assets = assets;
        _materials = materials;
        _owner = owner;
        _viewport = new SubViewport
        {
            Name = "ModelPreviewViewport",
            Size = new Vector2I(320, 320),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
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
            Name = "PreviewCamera",
            Current = true,
            Near = 0.01f,
            Far = 10000.0f,
            Environment = environment,
        };
        _light = new DirectionalLight3D
        {
            Name = "PreviewLight",
            LightEnergy = 1.4f,
            RotationDegrees = new Vector3(-45.0f, 35.0f, 0.0f),
        };

        _viewport.AddChild(_camera);
        _viewport.AddChild(_light);
        _owner.AddChild(_viewport);
    }

    public void Draw(string path, NVector2 size)
    {
        if (_viewport.Size.X != (int)size.X || _viewport.Size.Y != (int)size.Y)
        {
            _viewport.Size = new Vector2I(Math.Max(1, (int)size.X), Math.Max(1, (int)size.Y));
        }

        if (_path != path)
        {
            _path = path;
            _load = path.Length == 0 ? null : _assets.LoadModelAssetAsync(path);
            ClearModel();
        }

        if (_load is { IsCompletedSuccessfully: true, Result: { } model } && _loadedPath != _path)
        {
            ShowModel(model);
            _loadedPath = _path;
        }

        ImGui.Image((IntPtr)_viewport.GetTexture().GetRid().Id, size);
        if (_load == null)
        {
            Overlay(size, "No model");
        }
        else if (_load.IsFaulted)
        {
            Overlay(size, "Failed");
        }
        else if (!_load.IsCompleted)
        {
            Overlay(size, "Loading");
        }
        else if (_load.Result == null)
        {
            Overlay(size, "No preview");
        }
    }

    public void Dispose()
    {
        ClearModel();
        _viewport.QueueFree();
    }

    private void ShowModel(ModelAsset model)
    {
        ClearModel();
        _modelNode = model.Instantiate(_materials);
        _viewport.AddChild(_modelNode);
        Frame(model.LocalBounds);
    }

    private void ClearModel()
    {
        if (_modelNode != null)
        {
            _modelNode.QueueFree();
            _modelNode = null;
        }

        _loadedPath = "";
    }

    private void Frame(Aabb bounds)
    {
        Vector3 center = bounds.Position + bounds.Size * 0.5f;
        float radius = Math.Max(0.75f, bounds.Size.Length() * 0.5f);
        Vector3 direction = new Vector3(1.0f, 0.65f, 1.0f).Normalized();
        _camera.GlobalPosition = center + direction * radius * 2.6f;
        _camera.LookAt(center, Vector3.Up);
    }

    private static void Overlay(NVector2 size, string text)
    {
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        NVector2 min = ImGui.GetItemRectMin();
        NVector2 max = ImGui.GetItemRectMax();
        draw.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.WindowBg), 2.0f);
        NVector2 textSize = ImGui.CalcTextSize(text);
        draw.AddText(min + (size - textSize) * 0.5f, ImGui.GetColorU32(ImGuiCol.TextDisabled), text);
    }
}
