using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>Palette / Interface / Editor Colors tabs — all three are the same generic color-row
/// list over a different <see cref="StyleValueSpace"/>.</summary>
public sealed partial class StyleEditorWindow
{
    private string _paletteFilter = string.Empty;
    private string _interfaceFilter = string.Empty;
    private string _tokensFilter = string.Empty;
    private string _newPaletteName = string.Empty;

    private StyleValueSpace _refPromptSpace;
    private string? _refPromptId;
    private string _refPromptTarget = string.Empty;
    private bool _refPromptOpenRequested;

    private static readonly Dictionary<string, string> ImGuiColorGroupByName = BuildImGuiColorGroups();

    private void DrawPaletteTab()
    {
        ImGuiEx.FieldFilterInput("##palette-filter", ref _paletteFilter, "Filter palette...");
        FieldFilter filter = new(_paletteFilter);

        ImGui.SetNextItemWidth(200f);
        ImGui.InputTextWithHint("##new-palette-name", "New color name", ref _newPaletteName, 64);
        ImGui.SameLine();
        bool canAdd = !string.IsNullOrWhiteSpace(_newPaletteName) && !EditorStyle.Working.Palette.ContainsKey(_newPaletteName);
        ImGui.BeginDisabled(!canAdd);
        if (ImGui.SmallButton("Add"))
        {
            PushUndo();
            EditorStyle.Working.Palette[_newPaletteName] = new StyleColorLiteral(new Vector4(1f, 1f, 1f, 1f));
            EditorStyle.NotifyWorkingChanged();
            _newPaletteName = string.Empty;
        }

        ImGui.EndDisabled();
        ImGui.Separator();

        foreach (string name in EditorStyle.Active.Palette.Keys.OrderBy(static n => n, StringComparer.Ordinal).ToArray())
        {
            DrawColorRow(filter, StyleValueSpace.Palette, name, name);
        }

        DrawCustomReferencePopup();
    }

    private void DrawInterfaceTab()
    {
        ImGuiEx.FieldFilterInput("##interface-filter", ref _interfaceFilter, "Filter colors...");
        FieldFilter filter = new(_interfaceFilter);

        var byGroup = Enum.GetValues<ImGuiCol>()
            .Where(static c => c != ImGuiCol.COUNT)
            .GroupBy(static c => ImGuiColorGroupByName.GetValueOrDefault(c.ToString(), "Misc"))
            .OrderBy(static g => g.Key, StringComparer.Ordinal);

        foreach (var group in byGroup)
        {
            filter.Group(group.Key, () =>
            {
                foreach (ImGuiCol col in group.OrderBy(static c => c.ToString(), StringComparer.Ordinal))
                {
                    DrawColorRow(filter, StyleValueSpace.ImGuiColor, col.ToString(), col.ToString());
                }
            }, defaultOpen: true);
        }

        DrawCustomReferencePopup();
    }

    private void DrawTokensTab()
    {
        ImGuiEx.FieldFilterInput("##tokens-filter", ref _tokensFilter, "Filter tokens...");
        FieldFilter filter = new(_tokensFilter);

        if (StyleTokenRegistry.Colors.Count == 0)
        {
            ImGui.TextDisabled("No editor color tokens are registered.");
            return;
        }

        foreach (var group in StyleTokenRegistry.Colors.GroupBy(static c => c.Group).OrderBy(static g => g.Key, StringComparer.Ordinal))
        {
            filter.Group(group.Key, () =>
            {
                foreach (StyleColor token in group.OrderBy(static t => t.Label, StringComparer.Ordinal))
                {
                    DrawColorRow(filter, StyleValueSpace.Token, token.Id, token.Label);
                }
            }, defaultOpen: true);
        }

        DrawCustomReferencePopup();
    }

    /// <summary>One <c>[swatch] label [source ▾] overridden [Reset]</c> row, shared by all three
    /// color tabs — a token/imgui-color/palette entry are all just different sparse dicts on
    /// <see cref="EditorStyle.Working"/> with identical override semantics.</summary>
    private void DrawColorRow(FieldFilter filter, StyleValueSpace space, string id, string label)
    {
        filter.Field(label, () =>
        {
            ImGui.PushID(id);
            ImGui.PushID((int)space);

            Dictionary<string, StyleColorValue> dict = WorkingColors(space);
            bool overridden = dict.TryGetValue(id, out StyleColorValue? raw);
            Vector4 resolved = ResolvedColor(space, id);

            if (ImGui.ColorButton("##swatch", resolved, ImGuiColorEditFlags.AlphaPreviewHalf, new Vector2(20f, 20f)))
            {
                PushUndo();
                ImGui.OpenPopup("##picker");
            }

            if (ImGui.IsItemHovered())
            {
                BeginRowFlash(space, id);
                ImGui.SetTooltip(id);
            }

            if (ImGui.BeginPopup("##picker"))
            {
                Vector4 seed = raw is StyleColorLiteral literal ? literal.Color : resolved;
                if (ImGui.ColorPicker4("##pick", ref seed))
                {
                    SetColorOverride(space, id, new StyleColorLiteral(seed));
                }

                ImGui.EndPopup();
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(150f);
            DrawSourceCombo(space, id, raw, resolved);

            ImGui.SameLine();
            ImGui.TextUnformatted(label);

            ImGui.SameLine();
            ImGui.TextDisabled(overridden ? "overridden" : "inherited");

            ImGui.SameLine();
            ImGui.BeginDisabled(!overridden);
            if (ImGui.SmallButton("Reset"))
            {
                RemoveColorOverride(space, id);
            }

            ImGui.EndDisabled();

            ImGui.PopID();
            ImGui.PopID();
        });
    }

    private void DrawSourceCombo(StyleValueSpace space, string id, StyleColorValue? raw, Vector4 resolved)
    {
        string preview = raw switch
        {
            null => "(inherited)",
            StyleColorLiteral => "Literal",
            StyleColorReference reference => reference.Target,
            _ => "?",
        };

        if (!ImGui.BeginCombo("##source", preview))
        {
            return;
        }

        if (ImGui.Selectable("Literal", raw is StyleColorLiteral))
        {
            PushUndo();
            SetColorOverride(space, id, new StyleColorLiteral(resolved));
        }

        if (EditorStyle.Active.Palette.Count > 0)
        {
            ImGui.Separator();
            foreach (string name in EditorStyle.Active.Palette.Keys.OrderBy(static n => n, StringComparer.Ordinal))
            {
                if (space == StyleValueSpace.Palette && name == id)
                {
                    continue;
                }

                string target = $"${name}";
                if (ImGui.Selectable(target, raw is StyleColorReference r && r.Target == target))
                {
                    PushUndo();
                    SetColorOverride(space, id, new StyleColorReference(target, null, null, null));
                }
            }
        }

        ImGui.Separator();
        if (ImGui.Selectable("Custom reference..."))
        {
            OpenCustomReferencePrompt(space, id, raw as StyleColorReference);
        }

        ImGui.EndCombo();
    }

    private void OpenCustomReferencePrompt(StyleValueSpace space, string id, StyleColorReference? current)
    {
        _refPromptSpace = space;
        _refPromptId = id;
        _refPromptTarget = current?.Target ?? "@";
        _refPromptOpenRequested = true;
    }

    private void DrawCustomReferencePopup()
    {
        if (_refPromptId is null)
        {
            return;
        }

        const string popupId = "##StyleCustomRef";
        if (_refPromptOpenRequested)
        {
            ImGui.OpenPopup(popupId);
            _refPromptOpenRequested = false;
        }

        bool isOpen = true;
        ImGui.SetNextWindowSize(new Vector2(360f, 0f), ImGuiCond.Always);
        if (ImGui.BeginPopupModal(popupId, ref isOpen, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
        {
            ImGui.TextUnformatted("Custom reference");
            ImGui.TextWrapped("$name for a palette entry, @Name for an ImGui color, @token.id for another editor color token.");
            ImGui.SetNextItemWidth(-1f);
            bool submitted = ImGui.InputText("##ref-target", ref _refPromptTarget, 128, ImGuiInputTextFlags.EnterReturnsTrue);

            bool validPrefix = _refPromptTarget.Length > 1 && (_refPromptTarget[0] == '$' || _refPromptTarget[0] == '@');
            if (!validPrefix)
            {
                ImGuiEx.TextColored(CommonColors.Warning, "Must start with $ or @.");
            }

            ImGui.Spacing();
            bool ok = ImGui.Button("OK");
            ImGui.SameLine();
            bool cancel = ImGui.Button("Cancel");

            if ((submitted || ok) && validPrefix)
            {
                PushUndo();
                SetColorOverride(_refPromptSpace, _refPromptId, new StyleColorReference(_refPromptTarget, null, null, null));
                _refPromptId = null;
                ImGui.CloseCurrentPopup();
            }
            else if (cancel || !isOpen)
            {
                _refPromptId = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
        else
        {
            _refPromptId = null;
        }
    }

    private static Dictionary<string, string> BuildImGuiColorGroups()
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);

        void Add(string group, params string[] names)
        {
            foreach (string name in names)
            {
                map[name] = group;
            }
        }

        Add("Text", "Text", "TextDisabled", "TextSelectedBg", "TextLink");
        Add("Windows", "WindowBg", "ChildBg", "PopupBg", "Border", "BorderShadow", "ModalWindowDimBg");
        Add("Frames", "FrameBg", "FrameBgHovered", "FrameBgActive");
        Add("Titles/Menu", "TitleBg", "TitleBgActive", "TitleBgCollapsed", "MenuBarBg");
        Add("Scrollbar", "ScrollbarBg", "ScrollbarGrab", "ScrollbarGrabHovered", "ScrollbarGrabActive");
        Add("Widgets", "CheckMark", "SliderGrab", "SliderGrabActive", "DragDropTarget");
        Add("Buttons", "Button", "ButtonHovered", "ButtonActive");
        Add("Headers", "Header", "HeaderHovered", "HeaderActive");
        Add("Separators/Resize", "Separator", "SeparatorHovered", "SeparatorActive", "ResizeGrip", "ResizeGripHovered", "ResizeGripActive");
        Add("Tabs", "Tab", "TabHovered", "TabActive", "TabUnfocused", "TabUnfocusedActive", "TabSelected", "TabSelectedOverline", "TabDimmed", "TabDimmedSelected", "TabDimmedSelectedOverline");
        Add("Docking", "DockingPreview", "DockingEmptyBg");
        Add("Plots", "PlotLines", "PlotLinesHovered", "PlotHistogram", "PlotHistogramHovered");
        Add("Tables", "TableHeaderBg", "TableBorderStrong", "TableBorderLight", "TableRowBg", "TableRowBgAlt");
        Add("Navigation", "NavHighlight", "NavCursor", "NavWindowingHighlight", "NavWindowingDimBg");

        return map;
    }
}
