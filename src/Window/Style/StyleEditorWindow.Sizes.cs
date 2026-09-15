using System;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>The Sizes tab: every numeric <c>ImGuiStyle</c> field (<see cref="StyleVarFields"/>) plus
/// any registered <see cref="StyleSize"/> semantic tokens.</summary>
public sealed partial class StyleEditorWindow
{
    private string _sizesFilter = string.Empty;

    private void DrawSizesTab()
    {
        ImGuiEx.FieldFilterInput("##sizes-filter", ref _sizesFilter, "Filter sizes...");
        FieldFilter filter = new(_sizesFilter);

        filter.Group("ImGui Style", () =>
        {
            foreach (StyleVarFields.Field field in StyleVarFields.All.OrderBy(static f => f.Id, StringComparer.Ordinal))
            {
                DrawVarRow(filter, field);
            }
        }, defaultOpen: true);

        if (StyleTokenRegistry.Sizes.Count > 0)
        {
            foreach (var group in StyleTokenRegistry.Sizes.GroupBy(static s => s.Group).OrderBy(static g => g.Key, StringComparer.Ordinal))
            {
                filter.Group(group.Key, () =>
                {
                    foreach (StyleSize size in group.OrderBy(static s => s.Label, StringComparer.Ordinal))
                    {
                        DrawSizeTokenRow(filter, size);
                    }
                }, defaultOpen: true);
            }
        }
    }

    private void DrawVarRow(FieldFilter filter, StyleVarFields.Field field)
    {
        filter.Field(field.Id, () =>
        {
            ImGui.PushID(field.Id);

            bool overridden = EditorStyle.Working.Vars.TryGetValue(field.Id, out StyleVarValue raw);
            StyleVarValue resolved = EditorStyle.Active.Vars[field.Id];

            ImGui.SetNextItemWidth(220f);
            bool changed;
            Vector2 vec2 = resolved.AsVector2();
            float scalar = resolved.AsFloat();

            if (field.IsVector)
            {
                changed = ImGui.DragFloat2("##value", ref vec2, 0.1f);
            }
            else
            {
                changed = ImGui.DragFloat("##value", ref scalar, 0.1f, 0f, 256f);
            }

            if (ImGui.IsItemActivated())
            {
                PushUndo();
            }

            if (changed)
            {
                EditorStyle.Working.Vars[field.Id] = field.IsVector ? StyleVarValue.Vector(vec2) : StyleVarValue.Scalar(scalar);
                EditorStyle.NotifyWorkingChanged();
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(field.Id);

            ImGui.SameLine();
            ImGui.TextDisabled(overridden ? "overridden" : "inherited");

            ImGui.SameLine();
            ImGui.BeginDisabled(!overridden);
            if (ImGui.SmallButton("Reset"))
            {
                PushUndo();
                EditorStyle.Working.Vars.Remove(field.Id);
                EditorStyle.NotifyWorkingChanged();
            }

            ImGui.EndDisabled();
            ImGui.PopID();
        });
    }

    private void DrawSizeTokenRow(FieldFilter filter, StyleSize token)
    {
        filter.Field(token.Label, () =>
        {
            ImGui.PushID(token.Id);

            bool overridden = EditorStyle.Working.Sizes.ContainsKey(token.Id);
            float value = token.Value;

            ImGui.SetNextItemWidth(220f);
            bool changed = ImGui.DragFloat("##value", ref value, 0.1f, 0f, 256f);
            if (ImGui.IsItemActivated())
            {
                PushUndo();
            }

            if (changed)
            {
                EditorStyle.Working.Sizes[token.Id] = value;
                EditorStyle.NotifyWorkingChanged();
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(token.Label);

            ImGui.SameLine();
            ImGui.TextDisabled(overridden ? "overridden" : "inherited");

            ImGui.SameLine();
            ImGui.BeginDisabled(!overridden);
            if (ImGui.SmallButton("Reset"))
            {
                PushUndo();
                EditorStyle.Working.Sizes.Remove(token.Id);
                EditorStyle.NotifyWorkingChanged();
            }

            ImGui.EndDisabled();
            ImGui.PopID();
        });
    }
}
