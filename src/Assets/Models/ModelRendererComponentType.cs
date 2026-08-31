using ImGuiNET;

namespace WorldMapStudio;

[Subsystem(nameof(SceneComponentRegistry))]
public sealed class ModelRendererComponentType : ISceneComponentType
{
    private readonly AssetSystem _assets;
    private readonly MeshMaterialSystem _materials;
    private readonly ModelAssetPicker _picker;
    private readonly ModelTextureListPopup _textureList;

    public float Priority => 4.0f;

    public ModelRendererComponentType(SceneComponentRegistry registry)
    {
        _assets = registry.Context.Assets;
        _materials = registry.Context.MeshMaterials;
        _picker = new ModelAssetPicker(_assets, _materials, registry.Context.Root);
        _textureList = new ModelTextureListPopup(_assets, _materials);
    }

    public string TypeId => "model-renderer";

    public string DisplayName => "Model Renderer";

    public SceneComponent Create() => new ModelRendererComponent(_assets, _materials);

    public void DrawInspector(InspectorContext context, SceneComponent component)
    {
        var renderer = (ModelRendererComponent)component;

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Model:");
        ImGui.SameLine();
        if (ImGui.Button("Browse##model"))
        {
            _picker.Browse(renderer.ModelPath, selected =>
                ComponentFieldRecorder.Record(context, renderer, "model", renderer.ModelPath, selected, value => renderer.ModelPath = value));
        }

        if (renderer.ModelPath.Length > 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear##model"))
            {
                ComponentFieldRecorder.Record(context, renderer, "model", renderer.ModelPath, "", value => renderer.ModelPath = value);
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Textures##model") && renderer.TryGetModel(out ModelAsset? model))
            {
                _textureList.Open(model!);
            }
        }

        // Trails the buttons rather than leading them: the path can be arbitrarily long, and putting
        // it first pushed Browse/Clear off the edge of the inspector unless the panel was made wide.
        ImGui.SameLine();
        ImGui.TextDisabled(renderer.ModelPath.Length == 0 ? "(none)" : renderer.ModelPath);

        renderer.DrawInspectorExtra(context);
    }

    public void DrawModals()
    {
        _picker.Draw();
        _textureList.Draw();
    }
}
