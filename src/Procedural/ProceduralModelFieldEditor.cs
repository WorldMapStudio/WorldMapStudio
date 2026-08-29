using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// Draws a <see cref="ProceduralModel"/>'s function/parameters/format/material-slot fields. Shared by
/// <see cref="ProceduralComponentType"/>'s inspector and <see cref="ProceduralModelsWindow"/> so
/// the two do not duplicate this block — they differ only in what surrounds it (a "which model"
/// picker versus a catalog list), not in how a model's own fields are edited.
/// </summary>
public sealed class ProceduralModelFieldEditor
{
    private readonly ProceduralSystem _system;
    private readonly MeshParameterEditor _parameterEditor;

    public ProceduralModelFieldEditor(ProceduralSystem system, MeshParameterEditor parameterEditor)
    {
        _system = system;
        _parameterEditor = parameterEditor;
    }

    public void DrawModals() => _parameterEditor.DrawModals();

    public void Draw(EditSessionManager sessions, ProceduralModel model)
    {
        DrawFunction(sessions, model);

        IProceduralFunction? bound = _system.Find(model.FunctionId);
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

        DrawTransformPolicyWarning(model, bound);
        DrawParameters(sessions, model, bound);

        foreach (ProceduralOutputSlot output in bound.Outputs)
        {
            ImGui.PushID(output.Name);
            ImGui.Separator();
            if (bound.Outputs.Count > 1)
            {
                ImGui.TextDisabled(output.DisplayName);
            }

            DrawFormat(sessions, model, output);
            DrawMaterialSlots(sessions, model, output);
            ImGui.PopID();
        }

        if (bound.Outputs.Count == 0)
        {
            ImGui.Separator();
            ImGui.TextDisabled("(no model outputs)");
        }

        ImGui.Separator();
        ImGui.TextDisabled($"{model.Network.Vertices.Count} vertices, {model.Network.Edges.Count} edges");
    }

    private void DrawFunction(EditSessionManager sessions, ProceduralModel model)
    {
        IProceduralFunction? bound = _system.Find(model.FunctionId);
        string label = bound?.DisplayName ?? (model.FunctionId.Length == 0 ? "(none)" : $"{model.FunctionId} (missing)");

        if (!ImGui.BeginCombo("Function", label))
        {
            return;
        }

        foreach (IProceduralFunction function in _system.All)
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

    /// <summary>
    /// A bound function's transform policy (<see cref="IProceduralFunction.SelfRotation"/>,
    /// <see cref="IProceduralFunction.SelfScale"/>, <see cref="IProceduralFunction.UsesTerrainHeight"/>,
    /// <see cref="IProceduralFunction.PlanarNetwork"/>) applies to every placement of this model —
    /// switching to a more restrictive function (e.g. picking the road function on a model that used
    /// to be an unrestricted mesh) can silently change what an existing placement's transform means.
    /// Shown whenever the bound function restricts anything and the model has placements, not only at
    /// the moment the combo changes, so the risk is visible any time the model is reopened.
    /// </summary>
    private void DrawTransformPolicyWarning(ProceduralModel model, IProceduralFunction bound)
    {
        bool restricts = bound.SelfRotation != SelfRotation.Full
            || bound.SelfScale != SelfScale.PerAxis
            || bound.UsesTerrainHeight
            || bound.PlanarNetwork;
        if (!restricts)
        {
            return;
        }

        int uses = _system.UsageCount(model.RecordId ?? -1);
        if (uses == 0)
        {
            return;
        }

        ImGui.TextColored(new NVector4(1.0f, 0.72f, 0.22f, 1.0f),
            $"This function restricts placement (rotation/scale/terrain-following) — {uses} existing placement(s) may show a changed transform.");
    }

    private void DrawParameters(EditSessionManager sessions, ProceduralModel model, IProceduralFunction function)
    {
        string serialized = model.Parameters;
        MeshParameterValues values = MeshParameterValues.Parse(serialized);

        _parameterEditor.Draw(function.Parameters, values, serialized, (before, after) =>
            RecordChange(sessions, model, $"{function.DisplayName} parameters", before, after, value => model.Parameters = value));
    }

    private void DrawFormat(EditSessionManager sessions, ProceduralModel model, ProceduralOutputSlot output)
    {
        List<IModelFormat> available = AvailableFormats(output);
        if (available.Count <= 1)
        {
            return;
        }

        ProceduralFormats formats = ProceduralFormats.Parse(model.Formats);
        string? current = formats.Get(output);
        IModelFormat? currentFormat = current is { Length: > 0 } ? _system.Context.ModelFormats.Find(current) : null;
        string label = currentFormat?.DisplayName ?? "(auto)";
        if (!ImGui.BeginCombo("Format", label))
        {
            return;
        }

        foreach (IModelFormat format in available)
        {
            if (ImGui.Selectable(format.DisplayName, format.Id == current))
            {
                RecordFormatChange(sessions, model, output, format.Id);
            }
        }

        ImGui.EndCombo();
    }

    private List<IModelFormat> AvailableFormats(ProceduralOutputSlot output)
    {
        ModelFormatSystem formats = _system.Context.ModelFormats;
        IEnumerable<IModelFormat> candidates = output.SupportedFormats.Count == 0
            ? formats.All
            : output.SupportedFormats.Select(formats.Find).OfType<IModelFormat>();
        return candidates.Where(format => format.CanAuthor).ToList();
    }

    private static void RecordFormatChange(EditSessionManager sessions, ProceduralModel model, ProceduralOutputSlot output, string formatId)
    {
        string before = model.Formats;
        ProceduralFormats updated = ProceduralFormats.Parse(before);
        updated.Set(output, formatId);
        string after = updated.Serialize();
        RecordChange(sessions, model, $"{output.DisplayName} format", before, after, value => model.Formats = value);
    }

    private void DrawMaterialSlots(EditSessionManager sessions, ProceduralModel model, ProceduralOutputSlot output)
    {
        if (output.MaterialSlots.Count == 0)
        {
            return;
        }

        IModelFormat? format = _system.Context.ModelFormats.Find(_system.ResolveFormatId(model, output));
        IMeshMaterialType? materialType = format != null ? _system.Context.MeshMaterials.Find(format.MaterialTypeId) : null;
        string materialTypeId = materialType?.Id ?? StandardMeshMaterial.TypeId;
        ProceduralBindings bindings = ProceduralBindings.Parse(model.Materials);

        foreach (MeshMaterialSlot slot in output.MaterialSlots)
        {
            ImGui.PushID(slot.Name);
            DrawSlot(sessions, model, output, slot, materialTypeId, bindings);
            ImGui.PopID();
        }
    }

    private void DrawSlot(EditSessionManager sessions, ProceduralModel model, ProceduralOutputSlot output, MeshMaterialSlot slot, string materialTypeId, ProceduralBindings bindings)
    {
        ImGui.Separator();
        ImGui.TextDisabled(slot.DisplayName);

        List<MeshMaterialPreset> candidates = _system.Context.MeshMaterials.Presets.Where(preset => preset.TypeId == materialTypeId).ToList();
        int? boundPresetId = bindings.GetPresetId(output, slot);
        MeshMaterialPreset? bound = boundPresetId is { } id ? candidates.FirstOrDefault(preset => preset.RecordId == id) : null;
        string label = boundPresetId == null ? "(inline)" : bound?.Name ?? $"Preset #{boundPresetId} (missing)";

        if (ImGui.BeginCombo("Preset", label))
        {
            if (ImGui.Selectable("(inline)", boundPresetId == null))
            {
                RecordMaterialsChange(sessions, model, output, slot, b => b.Clear(output, slot));
            }

            foreach (MeshMaterialPreset preset in candidates)
            {
                if (ImGui.Selectable(preset.Name, preset.RecordId == boundPresetId))
                {
                    RecordMaterialsChange(sessions, model, output, slot, b => b.BindPreset(output, slot, preset.RecordId!.Value));
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

        MeshParameterValues inline = (bindings.GetInlineValues(output, slot) ?? materialType.Default.Values).Clone();
        string inlineBaseline = inline.Serialize();

        _parameterEditor.Draw(materialType.Parameters, inline, inlineBaseline, (_, afterInline) =>
        {
            string before = model.Materials;
            ProceduralBindings updated = ProceduralBindings.Parse(before);
            updated.BindInline(output, slot, MeshParameterValues.Parse(afterInline));
            string after = updated.Serialize();
            RecordChange(sessions, model, $"{slot.DisplayName} material", before, after, value => model.Materials = value);
        });
    }

    private static void RecordMaterialsChange(EditSessionManager sessions, ProceduralModel model, ProceduralOutputSlot output, MeshMaterialSlot slot, Action<ProceduralBindings> mutate)
    {
        string before = model.Materials;
        ProceduralBindings updated = ProceduralBindings.Parse(before);
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
