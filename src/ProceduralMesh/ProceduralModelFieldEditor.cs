using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// Draws a <see cref="ProceduralModel"/>'s function/parameters/format/material-slot fields. Shared by
/// <see cref="ProceduralMeshComponentType"/>'s inspector and <see cref="ProceduralModelsWindow"/> so
/// the two do not duplicate this block — they differ only in what surrounds it (a "which model"
/// picker versus a catalog list), not in how a model's own fields are edited.
/// </summary>
public sealed class ProceduralModelFieldEditor
{
    private readonly ProceduralMeshSystem _system;
    private readonly MeshParameterEditor _parameterEditor;

    public ProceduralModelFieldEditor(ProceduralMeshSystem system, MeshParameterEditor parameterEditor)
    {
        _system = system;
        _parameterEditor = parameterEditor;
    }

    public void DrawModals() => _parameterEditor.DrawModals();

    public void Draw(EditSessionManager sessions, ProceduralModel model)
    {
        DrawFunction(sessions, model);

        IProceduralMeshFunction? bound = _system.Find(model.FunctionId);
        if (bound == null)
        {
            if (model.FunctionId.Length > 0)
            {
                ImGui.TextColored(new NVector4(1.0f, 0.45f, 0.4f, 1.0f), $"No loaded function provides '{model.FunctionId}'.");
            }

            return;
        }

        string graphMode = bound.AllowsMultipleGraphs ? "multiple graphs" : "single graph";
        string branchMode = bound.AllowsBranching ? "branching" : "linear";
        ImGui.TextDisabled($"v{bound.Version}, {graphMode}, {branchMode}");
        foreach (string problem in model.Network.ValidateFor(bound.DisplayName, bound.AllowsMultipleGraphs, bound.AllowsBranching))
        {
            ImGui.TextColored(new NVector4(1.0f, 0.72f, 0.22f, 1.0f), problem);
        }

        DrawParameters(sessions, model, bound);
        DrawFormat(sessions, model, bound);
        DrawMaterialSlots(sessions, model, bound);

        ImGui.Separator();
        ImGui.TextDisabled($"{model.Network.Vertices.Count} vertices, {model.Network.Edges.Count} edges");
    }

    private void DrawFunction(EditSessionManager sessions, ProceduralModel model)
    {
        IProceduralMeshFunction? bound = _system.Find(model.FunctionId);
        string label = bound?.DisplayName ?? (model.FunctionId.Length == 0 ? "(none)" : $"{model.FunctionId} (missing)");

        if (!ImGui.BeginCombo("Function", label))
        {
            return;
        }

        foreach (IProceduralMeshFunction function in _system.All)
        {
            if (ImGui.Selectable($"{function.DisplayName}##{function.Id}", function.Id == model.FunctionId))
            {
                RecordChange(sessions, model, "function", model.FunctionId, function.Id, value => model.FunctionId = value);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(function.Description);
            }
        }

        ImGui.EndCombo();
    }

    private void DrawParameters(EditSessionManager sessions, ProceduralModel model, IProceduralMeshFunction function)
    {
        string serialized = model.Parameters;
        MeshParameterValues values = MeshParameterValues.Parse(serialized);

        _parameterEditor.Draw(function.Parameters, values, serialized, (before, after) =>
            RecordChange(sessions, model, $"{function.DisplayName} parameters", before, after, value => model.Parameters = value));
    }

    private void DrawFormat(EditSessionManager sessions, ProceduralModel model, IProceduralMeshFunction function)
    {
        List<IModelFormat> available = AvailableFormats(function);
        if (available.Count <= 1)
        {
            return;
        }

        ImGui.Separator();
        IModelFormat? current = _system.Context.ModelFormats.Find(model.FormatId);
        string label = current?.DisplayName ?? "(auto)";
        if (!ImGui.BeginCombo("Format", label))
        {
            return;
        }

        foreach (IModelFormat format in available)
        {
            if (ImGui.Selectable(format.DisplayName, format.Id == model.FormatId))
            {
                RecordChange(sessions, model, "format", model.FormatId, format.Id, value => model.FormatId = value);
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

    private void DrawMaterialSlots(EditSessionManager sessions, ProceduralModel model, IProceduralMeshFunction function)
    {
        if (function.MaterialSlots.Count == 0)
        {
            return;
        }

        IModelFormat? format = _system.Context.ModelFormats.Find(_system.ResolveFormatId(model));
        IMeshMaterialType? materialType = format != null ? _system.Context.MeshMaterials.Find(format.MaterialTypeId) : null;
        string materialTypeId = materialType?.Id ?? StandardMeshMaterial.TypeId;
        ProceduralMeshMaterialBindings bindings = ProceduralMeshMaterialBindings.Parse(model.Materials);

        foreach (MeshMaterialSlot slot in function.MaterialSlots)
        {
            ImGui.PushID(slot.Name);
            DrawSlot(sessions, model, slot, materialTypeId, bindings);
            ImGui.PopID();
        }
    }

    private void DrawSlot(EditSessionManager sessions, ProceduralModel model, MeshMaterialSlot slot, string materialTypeId, ProceduralMeshMaterialBindings bindings)
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
                RecordMaterialsChange(sessions, model, slot, b => b.Clear(slot));
            }

            foreach (MeshMaterialPreset preset in candidates)
            {
                if (ImGui.Selectable(preset.Name, preset.RecordId == boundPresetId))
                {
                    RecordMaterialsChange(sessions, model, slot, b => b.BindPreset(slot, preset.RecordId!.Value));
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
            string before = model.Materials;
            ProceduralMeshMaterialBindings updated = ProceduralMeshMaterialBindings.Parse(before);
            updated.BindInline(slot, MeshParameterValues.Parse(afterInline));
            string after = updated.Serialize();
            RecordChange(sessions, model, $"{slot.DisplayName} material", before, after, value => model.Materials = value);
        });
    }

    private static void RecordMaterialsChange(EditSessionManager sessions, ProceduralModel model, MeshMaterialSlot slot, Action<ProceduralMeshMaterialBindings> mutate)
    {
        string before = model.Materials;
        ProceduralMeshMaterialBindings updated = ProceduralMeshMaterialBindings.Parse(before);
        mutate(updated);
        string after = updated.Serialize();
        RecordChange(sessions, model, $"{slot.DisplayName} material", before, after, value => model.Materials = value);
    }

    // A combo/parameter change has no activate/deactivate pair to bracket, so it is recorded on the
    // spot, exactly like MeshMaterialsWindow.RecordNow.
    private static void RecordChange<T>(EditSessionManager sessions, ProceduralModel model, string field, T before, T after, Action<T> set)
    {
        if (EqualityComparer<T>.Default.Equals(before, after))
        {
            return;
        }

        var command = new SetFieldCommand<T>(model, field, set, before, after);
        command.Apply();
        sessions.Record(command);
    }
}
