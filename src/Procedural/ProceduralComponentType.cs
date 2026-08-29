using ImGuiNET;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

[Subsystem(nameof(SceneComponentRegistry))]
public sealed class ProceduralComponentType : ISceneComponentType
{
    private readonly ProceduralSystem _system;
    private readonly ProceduralModelFieldEditor _fields;
    private readonly ProceduralModelPicker _picker;

    public float Priority => 5.0f;

    public ProceduralComponentType(SceneComponentRegistry registry)
    {
        _system = registry.Context.Procedural;
        _fields = new ProceduralModelFieldEditor(_system, new MeshParameterEditor(new TextureAssetPicker(registry.Context.Assets), registry.Context.Landscape));
        _picker = new ProceduralModelPicker(_system, registry.Context.Root);
    }

    public string TypeId => "procedural-mesh";

    public string DisplayName => "Procedural Mesh";

    public SceneComponent Create() => new ProceduralComponent(_system);

    public void DrawInspector(InspectorContext context, SceneComponent component)
    {
        var procedural = (ProceduralComponent)component;

        DrawModelReference(context, procedural);

        ProceduralModel? model = procedural.Model;
        if (model == null)
        {
            if (procedural.ModelId is { } danglingId)
            {
                ImGui.TextColored(new NVector4(1.0f, 0.45f, 0.4f, 1.0f), $"No loaded model provides #{danglingId}.");
            }

            return;
        }

        ImGui.Separator();
        _fields.Draw(context.Sessions, model);
    }

    public void DrawModals()
    {
        _fields.DrawModals();
        _picker.Draw();
    }

    private void DrawModelReference(InspectorContext context, ProceduralComponent procedural)
    {
        ProceduralModel? model = procedural.Model;
        string label = model != null
            ? $"{model.Name} (#{model.RecordId})"
            : procedural.ModelId == null ? "(none)" : $"#{procedural.ModelId} (missing)";

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Model:");
        ImGui.SameLine();
        ImGui.TextDisabled(label);

        if (ImGui.Button("Browse##model"))
        {
            _picker.Browse(procedural.ModelId, selected =>
                ComponentFieldRecorder.Record(context, procedural, "model", procedural.ModelId, selected, value => procedural.ModelId = value));
        }

        ImGui.SameLine();
        if (ImGui.Button("New##model"))
        {
            _picker.OpenCreate(context.Sessions, _system.Context.Catalog, created =>
                ComponentFieldRecorder.Record(context, procedural, "model", procedural.ModelId, created.RecordId, value => procedural.ModelId = value));
        }

        if (procedural.ModelId != null)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear##model"))
            {
                ComponentFieldRecorder.Record(context, procedural, "model", procedural.ModelId, null, value => procedural.ModelId = value);
            }
        }

        if (model != null)
        {
            int uses = _system.UsageCount(model.RecordId ?? -1);
            if (uses > 1)
            {
                ImGui.TextDisabled($"used by {uses} entities in this map");
            }
        }
    }
}
