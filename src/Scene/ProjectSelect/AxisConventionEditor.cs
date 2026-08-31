using System;
using System.Linq;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Draws the per-axis editor for a project's <see cref="AxisConvention"/>: one combo per user
/// axis picking which signed Godot direction it points along. Assignments stay a valid
/// permutation automatically (picking a taken direction swaps the two), so the mapping is always
/// invertible. Returns true on the frames the mapping changed.
/// </summary>
public static class AxisConventionEditor
{
    private static readonly SignedAxis[] Options =
    [
        SignedAxis.PosX, SignedAxis.NegX,
        SignedAxis.PosY, SignedAxis.NegY,
        SignedAxis.PosZ, SignedAxis.NegZ,
    ];

    private static readonly string[] UserAxisNames = ["User X", "User Y", "User Z"];

    public static bool Draw(AxisConvention convention)
    {
        bool changed = DrawPresetRow(convention);

        ImGui.TextDisabled("Which Godot direction each of your axes points along.");

        for (int userAxis = 0; userAxis < 3; userAxis++)
        {
            changed |= DrawAxisRow(convention, userAxis);
        }

        ImGui.Spacing();
        if (convention.IsGodotDefault)
        {
            ImGui.TextDisabled("Matches Godot's default axes (no remapping).");
        }

        return changed;
    }

    private static bool DrawPresetRow(AxisConvention convention)
    {
        IAxisConventionPreset[] presets = AxisConventionPresets.Instance.Presets.ToArray();
        if (presets.Length == 0)
        {
            return false;
        }

        bool changed = false;

        ImGui.SetNextItemWidth(220);
        if (ImGui.BeginCombo("Preset", "Apply a preset..."))
        {
            foreach (IAxisConventionPreset preset in presets)
            {
                if (ImGui.Selectable(preset.Name))
                {
                    convention.Apply(preset.CreateConvention());
                    changed = true;
                }

                if (ImGui.IsItemHovered() && preset.Description.Length > 0)
                {
                    ImGui.SetTooltip(preset.Description);
                }
            }

            ImGui.EndCombo();
        }

        ImGui.Spacing();
        return changed;
    }

    private static bool DrawAxisRow(AxisConvention convention, int userAxis)
    {
        ImGui.PushID(userAxis);
        try
        {
            SignedAxis current = convention.Get(userAxis);

            ImGui.SetNextItemWidth(160);
            if (!ImGui.BeginCombo(UserAxisNames[userAxis], current.Label()))
            {
                return false;
            }

            bool changed = false;
            foreach (SignedAxis option in Options)
            {
                bool selected = option == current;
                if (ImGui.Selectable(option.Label(), selected) && option != current)
                {
                    convention.AssignUserAxis(userAxis, option);
                    changed = true;
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
            return changed;
        }
        finally
        {
            ImGui.PopID();
        }
    }
}
