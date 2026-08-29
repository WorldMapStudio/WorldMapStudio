using ImGuiNET;

namespace WorldMapStudio;

[Subsystem(nameof(SceneComponentRegistry))]
public sealed class ModelRendererComponentType : ISceneComponentType
{
    private readonly AssetSystem _assets;
    private readonly MeshMaterialSystem _materials;
    private readonly ModelAssetPicker _picker;

    public float Priority => 4.0f;

    public ModelRendererComponentType(SceneComponentRegistry registry)
    {
        _assets = registry.Context.Assets;
        _materials = registry.Context.MeshMaterials;
        _picker = new ModelAssetPicker(_assets, _materials, registry.Context.Root);
    }

    public string TypeId => "model-renderer";

    public string DisplayName => "Model Renderer";

    public SceneComponent Create() => new ModelRendererComponent(_assets, _materials);

    public void DrawInspector(InspectorContext context, SceneComponent component)
    {
        var renderer = (ModelRendererComponent)component;

        DrawAssetPath("Model", renderer.ModelPath);
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
        }

        renderer.DrawInspectorExtra(context);
    }

    public void DrawModals() => _picker.Draw();

    private static void DrawAssetPath(string label, string path)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{label}:");
        ImGui.SameLine();
        ImGui.TextDisabled(path.Length == 0 ? "(none)" : path);
    }
}
