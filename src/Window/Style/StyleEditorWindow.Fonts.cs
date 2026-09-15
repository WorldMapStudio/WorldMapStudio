using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>The Fonts tab: global scale, then one section per registered <see cref="StyleFont"/> slot.</summary>
public sealed partial class StyleEditorWindow
{
    private enum FontSourceKind
    {
        Default,
        System,
        File,
    }

    private void DrawFontsTab()
    {
        DrawFontScaleRow();
        ImGui.Separator();

        foreach (StyleFont font in StyleTokenRegistry.Fonts.OrderBy(static f => f.Id, StringComparer.Ordinal))
        {
            DrawFontSlot(font);
            ImGui.Separator();
        }
    }

    private void DrawFontScaleRow()
    {
        bool overridden = EditorStyle.Working.Fonts.Scale is not null;
        float scale = EditorStyle.Active.FontScale;

        ImGui.SetNextItemWidth(160f);
        bool changed = ImGui.DragFloat("Global Scale##font-scale", ref scale, 0.01f, 0.5f, 3.0f);
        if (ImGui.IsItemActivated())
        {
            PushUndo();
        }

        if (changed)
        {
            EditorStyle.Working.Fonts.Scale = MathF.Max(0.1f, scale);
            EditorStyle.NotifyWorkingChanged();
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(!overridden);
        if (ImGui.SmallButton("Reset##font-scale"))
        {
            PushUndo();
            EditorStyle.Working.Fonts.Scale = null;
            EditorStyle.NotifyWorkingChanged();
        }

        ImGui.EndDisabled();
    }

    private void DrawFontSlot(StyleFont font)
    {
        ImGui.PushID(font.Id);

        bool ownOverride = EditorStyle.Working.Fonts.Slots.TryGetValue(font.Id, out StyleFontSlotValue? own);
        EditorStyle.Active.FontSlots.TryGetValue(font.Id, out StyleFontSlotValue? resolvedSlot);

        ImGuiEx.TextColored(CommonColors.Accent, font.Id);
        ImGui.SameLine();
        ImGui.TextDisabled(ownOverride ? "overridden" : "inherited");
        ImGui.SameLine();
        ImGui.BeginDisabled(!ownOverride);
        if (ImGui.SmallButton("Reset"))
        {
            PushUndo();
            EditorStyle.Working.Fonts.Slots.Remove(font.Id);
            EditorStyle.NotifyWorkingChanged();
        }

        ImGui.EndDisabled();

        FontSourceKind kind = resolvedSlot?.File is { Length: > 0 }
            ? FontSourceKind.File
            : resolvedSlot?.Family is { Length: > 0 }
                ? FontSourceKind.System
                : FontSourceKind.Default;

        int kindIndex = (int)kind;
        ImGui.SetNextItemWidth(160f);
        if (ImGui.Combo("Source", ref kindIndex, "Built-in Default\0System Font\0File\0"))
        {
            PushUndo();
            FontSourceKind newKind = (FontSourceKind)kindIndex;
            StyleFontSlotValue draft = own ?? new StyleFontSlotValue { Size = resolvedSlot?.Size ?? font.DefaultSize };
            EditorStyle.Working.Fonts.Slots[font.Id] = newKind switch
            {
                FontSourceKind.Default => new StyleFontSlotValue { Size = draft.Size },
                FontSourceKind.System => new StyleFontSlotValue { Family = font.DefaultFamily, Size = draft.Size },
                FontSourceKind.File => new StyleFontSlotValue { File = draft.File ?? string.Empty, Size = draft.Size },
                _ => draft,
            };

            EditorStyle.NotifyWorkingChanged();
        }

        switch (kind)
        {
            case FontSourceKind.System:
                DrawSystemFontControls(font, resolvedSlot);
                break;
            case FontSourceKind.File:
                DrawFileFontControls(font, resolvedSlot);
                break;
        }

        DrawFontSizeRow(font, resolvedSlot);
        DrawFontSample(font);

        ImGui.PopID();
    }

    private void DrawSystemFontControls(StyleFont font, StyleFontSlotValue? resolvedSlot)
    {
        SystemFontCatalog.EnsureScanStarted();

        if (SystemFontCatalog.IsScanning)
        {
            ImGui.TextDisabled("Scanning system fonts...");
            return;
        }

        System.Collections.Generic.IReadOnlyList<SystemFontFamilyInfo>? families = SystemFontCatalog.Families;
        if (families is null || families.Count == 0)
        {
            ImGui.TextDisabled("No system fonts found.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Refresh"))
            {
                SystemFontCatalog.Refresh();
            }

            return;
        }

        string currentFamily = resolvedSlot?.Family ?? font.DefaultFamily;

        ImGui.SetNextItemWidth(240f);
        if (ImGui.BeginCombo("Family", currentFamily))
        {
            foreach (SystemFontFamilyInfo family in families)
            {
                ImGui.BeginDisabled(!family.Supported);
                bool selected = ImGui.Selectable(family.Family, family.Family == currentFamily);
                ImGui.EndDisabled();

                if (!family.Supported && ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(family.UnsupportedReason ?? "Unsupported font format.");
                }

                if (selected && family.Supported)
                {
                    ApplyFontSlot(font, slot => slot with { Family = family.Family, Weight = null, Italic = null, File = null });
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Refresh"))
        {
            SystemFontCatalog.Refresh();
        }

        System.Collections.Generic.IReadOnlyList<SystemFontVariant> variants = SystemFontCatalog.Variants(currentFamily);
        int currentWeight = resolvedSlot?.Weight ?? 400;
        bool currentItalic = resolvedSlot?.Italic ?? false;

        int[] weights = variants.Select(static v => v.Weight).Distinct().OrderBy(static w => w).ToArray();
        if (weights.Length > 0)
        {
            ImGui.SetNextItemWidth(120f);
            string weightPreview = currentWeight.ToString();
            if (ImGui.BeginCombo("Weight", weightPreview))
            {
                foreach (int weight in weights)
                {
                    if (ImGui.Selectable(weight.ToString(), weight == currentWeight))
                    {
                        int capturedWeight = weight;
                        ApplyFontSlot(font, slot => slot with { Family = currentFamily, Weight = capturedWeight, File = null });
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine();
        }

        bool italic = currentItalic;
        if (ImGui.Checkbox("Italic", ref italic))
        {
            PushUndo();
            ApplyFontSlot(font, slot => slot with { Family = currentFamily, Italic = italic, File = null });
        }
    }

    private void DrawFileFontControls(StyleFont font, StyleFontSlotValue? resolvedSlot)
    {
        string path = resolvedSlot?.File ?? string.Empty;
        ImGui.SetNextItemWidth(-70f);
        bool changed = ImGui.InputText("##file-path", ref path, 512);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            ApplyFontSlot(font, slot => slot with { File = path, Family = null, Weight = null, Italic = null });
        }
        else if (changed)
        {
            // Live-typed characters preview immediately without spamming the undo stack per keystroke.
            EditorStyle.Working.Fonts.Slots[font.Id] = (EditorStyle.Working.Fonts.Slots.GetValueOrDefault(font.Id) ?? new StyleFontSlotValue())
                with
            { File = path, Family = null, Weight = null, Italic = null };
            EditorStyle.NotifyWorkingChanged();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Browse...") && NativeFileDialog.IsSupported)
        {
            NativeFileDialog.PickFile("Choose Font File", ["*.ttf,*.otf,*.ttc ; Fonts"], string.Empty, picked =>
            {
                if (picked is null)
                {
                    return;
                }

                ApplyFontSlot(font, slot => slot with { File = picked, Family = null, Weight = null, Italic = null });
            });
        }
    }

    private void DrawFontSizeRow(StyleFont font, StyleFontSlotValue? resolvedSlot)
    {
        float size = resolvedSlot?.Size ?? font.DefaultSize;
        ImGui.SetNextItemWidth(120f);
        bool changed = ImGui.DragFloat("Size", ref size, 0.25f, 6f, 96f);
        if (ImGui.IsItemActivated())
        {
            PushUndo();
        }

        if (changed)
        {
            ApplyFontSlotNoUndo(font, slot => slot with { Size = MathF.Max(4f, size) });
        }
    }

    private void DrawFontSample(StyleFont font)
    {
        using (ImGuiEx.PushFont(font))
        {
            ImGui.TextUnformatted("The quick brown fox jumps over the lazy dog. 0123456789 {}[]");
        }
    }

    /// <summary>Pushes undo, then applies <paramref name="mutate"/> to the slot's current state (own
    /// override if any, else a blank slot).</summary>
    private void ApplyFontSlot(StyleFont font, Func<StyleFontSlotValue, StyleFontSlotValue> mutate)
    {
        PushUndo();
        ApplyFontSlotNoUndo(font, mutate);
    }

    private void ApplyFontSlotNoUndo(StyleFont font, Func<StyleFontSlotValue, StyleFontSlotValue> mutate)
    {
        StyleFontSlotValue current = EditorStyle.Working.Fonts.Slots.GetValueOrDefault(font.Id) ?? new StyleFontSlotValue();
        EditorStyle.Working.Fonts.Slots[font.Id] = mutate(current);
        EditorStyle.NotifyWorkingChanged();
    }
}
