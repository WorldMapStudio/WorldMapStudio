using System.Collections.Generic;
using System.Linq;
using Godot;
using ImGuiNET;

namespace WorldMapStudio;

[Subsystem(nameof(SceneComponentRegistry))]
public sealed class ProceduralMeshComponentType : ISceneComponentType
{
    private readonly ProceduralMeshSystem _system;
    private readonly MeshParameterEditor _parameterEditor;

    public float Priority => 5.0f;

    public ProceduralMeshComponentType(SceneComponentRegistry registry)
    {
        _system = registry.Context.ProceduralMeshes;
        _parameterEditor = new MeshParameterEditor(new TextureAssetPicker(registry.Context.Assets));
    }

    public string TypeId => "procedural-mesh";

    public string DisplayName => "Procedural Mesh";

    public SceneComponent Create()
    {
        var component = new ProceduralMeshComponent(_system);
        int a = component.Network.AddVertex(new Vector3(-1.0f, 0.0f, 0.0f));
        int b = component.Network.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));
        component.Network.AddEdge(a, b);
        return component;
    }

    public void DrawInspector(InspectorContext context, SceneComponent component)
    {
        var procedural = (ProceduralMeshComponent)component;

        IProceduralMeshFunction? bound = _system.Find(procedural.FunctionId);
        string label = bound?.DisplayName ?? (procedural.FunctionId.Length == 0 ? "(none)" : $"{procedural.FunctionId} (missing)");

        if (ImGui.BeginCombo("Function", label))
        {
            foreach (IProceduralMeshFunction function in _system.All)
            {
                if (ImGui.Selectable($"{function.DisplayName}##{function.Id}", function.Id == procedural.FunctionId))
                {
                    ComponentFieldRecorder.Record(context, procedural, "function", procedural.FunctionId, function.Id, value => procedural.FunctionId = value);
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(function.Description);
                }
            }

            ImGui.EndCombo();
        }

        if (bound == null)
        {
            if (procedural.FunctionId.Length > 0)
            {
                ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.45f, 0.4f, 1.0f), $"No loaded function provides '{procedural.FunctionId}'.");
            }
        }
        else
        {
            string graphMode = bound.AllowsMultipleGraphs ? "multiple graphs" : "single graph";
            string branchMode = bound.AllowsBranching ? "branching" : "linear";
            ImGui.TextDisabled($"v{bound.Version}, {graphMode}, {branchMode}");
            foreach (string problem in procedural.Network.ValidateFor(bound.DisplayName, bound.AllowsMultipleGraphs, bound.AllowsBranching))
            {
                ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.72f, 0.22f, 1.0f), problem);
            }

            DrawParameters(context, procedural, bound);
            DrawFormat(context, procedural, bound);
            DrawMaterialSlots(context, procedural, bound);
        }

        ImGui.Separator();
        ImGui.TextDisabled($"{procedural.Network.Vertices.Count} vertices, {procedural.Network.Edges.Count} edges");
    }

    public void DrawModals() => _parameterEditor.DrawModals();

    private void DrawParameters(InspectorContext context, ProceduralMeshComponent component, IProceduralMeshFunction function)
    {
        string serialized = component.Parameters;
        MeshParameterValues values = MeshParameterValues.Parse(serialized);

        _parameterEditor.Draw(function.Parameters, values, serialized, (before, after) =>
            ComponentFieldRecorder.Record(context, component, $"{function.DisplayName} parameters", before, after, value => component.Parameters = value));
    }

    private void DrawFormat(InspectorContext context, ProceduralMeshComponent component, IProceduralMeshFunction function)
    {
        List<IModelFormat> available = AvailableFormats(function);
        if (available.Count <= 1)
        {
            return;
        }

        ImGui.Separator();
        IModelFormat? current = _system.Context.ModelFormats.Find(component.FormatId);
        string label = current?.DisplayName ?? "(auto)";
        if (!ImGui.BeginCombo("Format", label))
        {
            return;
        }

        foreach (IModelFormat format in available)
        {
            if (ImGui.Selectable(format.DisplayName, format.Id == component.FormatId))
            {
                ComponentFieldRecorder.Record(context, component, "format", component.FormatId, format.Id, value => component.FormatId = value);
            }
        }

        ImGui.EndCombo();
    }

    private List<IModelFormat> AvailableFormats(IProceduralMeshFunction function)
    {
        ModelFormatSystem formats = _system.Context.ModelFormats;
        IEnumerable<IModelFormat> candidates = function.SupportedFormats.Count == 0
            ? formats.All
            : function.SupportedFormats.Select(formats.Find).OfType<IModelFormat>();
        return candidates.Where(format => format.CanAuthor).ToList();
    }

    private void DrawMaterialSlots(InspectorContext context, ProceduralMeshComponent component, IProceduralMeshFunction function)
    {
        if (function.MaterialSlots.Count == 0)
        {
            return;
        }

        IModelFormat? format = _system.Context.ModelFormats.Find(_system.ResolveFormatId(component));
        IMeshMaterialType? materialType = format != null ? _system.Context.MeshMaterials.Find(format.MaterialTypeId) : null;
        string materialTypeId = materialType?.Id ?? StandardMeshMaterial.TypeId;
        ProceduralMeshMaterialBindings bindings = ProceduralMeshMaterialBindings.Parse(component.Materials);

        foreach (MeshMaterialSlot slot in function.MaterialSlots)
        {
            ImGui.PushID(slot.Name);
            DrawSlot(context, component, slot, materialTypeId, bindings);
            ImGui.PopID();
        }
    }

    private void DrawSlot(InspectorContext context, ProceduralMeshComponent component, MeshMaterialSlot slot, string materialTypeId, ProceduralMeshMaterialBindings bindings)
    {
        ImGui.Separator();
        ImGui.TextDisabled(slot.DisplayName);

        List<MeshMaterialPreset> candidates = _system.Context.MeshMaterials.Presets.Where(preset => preset.TypeId == materialTypeId).ToList();
        int? boundPresetId = bindings.GetPresetId(slot);
        MeshMaterialPreset? bound = boundPresetId is { } id ? candidates.FirstOrDefault(preset => preset.RecordId == id) : null;
        string label = boundPresetId == null ? "(inline)" : bound?.Name ?? $"Preset #{boundPresetId} (missing)";

        if (ImGui.BeginCombo("Preset", label))
        {
            if (ImGui.Selectable("(inline)", boundPresetId == null))
            {
                RecordMaterialsChange(context, component, slot, b => b.Clear(slot));
            }

            foreach (MeshMaterialPreset preset in candidates)
            {
                if (ImGui.Selectable(preset.Name, preset.RecordId == boundPresetId))
                {
                    RecordMaterialsChange(context, component, slot, b => b.BindPreset(slot, preset.RecordId!.Value));
                }
            }

            ImGui.EndCombo();
        }

        if (boundPresetId != null)
        {
            return;
        }

        IMeshMaterialType? materialType = _system.Context.MeshMaterials.Find(materialTypeId);
        if (materialType == null)
        {
            return;
        }

        MeshParameterValues inline = (bindings.GetInlineValues(slot) ?? materialType.Default.Values).Clone();
        string inlineBaseline = inline.Serialize();

        _parameterEditor.Draw(materialType.Parameters, inline, inlineBaseline, (_, afterInline) =>
        {
            string before = component.Materials;
            ProceduralMeshMaterialBindings updated = ProceduralMeshMaterialBindings.Parse(before);
            updated.BindInline(slot, MeshParameterValues.Parse(afterInline));
            string after = updated.Serialize();
            ComponentFieldRecorder.Record(context, component, $"{slot.DisplayName} material", before, after, value => component.Materials = value);
        });
    }

    private static void RecordMaterialsChange(InspectorContext context, ProceduralMeshComponent component, MeshMaterialSlot slot, System.Action<ProceduralMeshMaterialBindings> mutate)
    {
        string before = component.Materials;
        ProceduralMeshMaterialBindings updated = ProceduralMeshMaterialBindings.Parse(before);
        mutate(updated);
        string after = updated.Serialize();
        ComponentFieldRecorder.Record(context, component, $"{slot.DisplayName} material", before, after, value => component.Materials = value);
    }
}
