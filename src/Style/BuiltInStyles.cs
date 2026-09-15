using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The three roots every style ultimately extends: Dark, Light, Classic. Synthesized from ImGui's
/// own <c>StyleColors*</c> presets applied to a scratch <c>ImGuiStyle</c> (never the live one), so
/// there is always a complete base to fall back to — not files, and not touched by resolution.
/// </summary>
public static class BuiltInStyles
{
    public sealed class Preset
    {
        public required string Name { get; init; }
        public required IReadOnlyDictionary<ImGuiCol, Vector4> Colors { get; init; }
        public required IReadOnlyDictionary<string, StyleVarValue> Vars { get; init; }
    }

    private static readonly Lazy<IReadOnlyDictionary<string, Preset>> LazyPresets = new(Build);

    public static IReadOnlyCollection<string> Names => (IReadOnlyCollection<string>)LazyPresets.Value.Keys;

    public static bool IsBuiltIn(string name) => LazyPresets.Value.ContainsKey(name);

    public static Preset? Find(string name) => LazyPresets.Value.GetValueOrDefault(name);

    private static IReadOnlyDictionary<string, Preset> Build()
    {
        return new Dictionary<string, Preset>(StringComparer.Ordinal)
        {
            ["Dark"] = Capture("Dark", ImGui.StyleColorsDark),
            ["Light"] = Capture("Light", ImGui.StyleColorsLight),
            ["Classic"] = Capture("Classic", ImGui.StyleColorsClassic),
        };
    }

    private static unsafe Preset Capture(string name, Action<ImGuiStylePtr> apply)
    {
        ImGuiStyle* native = ImGuiNative.ImGuiStyle_ImGuiStyle();
        try
        {
            ImGuiStylePtr style = new(native);
            apply(style);

            Dictionary<ImGuiCol, Vector4> colors = new();
            foreach (ImGuiCol col in Enum.GetValues<ImGuiCol>())
            {
                if (col == ImGuiCol.COUNT)
                {
                    continue;
                }

                colors[col] = style.Colors[(int)col];
            }

            Dictionary<string, StyleVarValue> vars = new(StringComparer.Ordinal);
            foreach (StyleVarFields.Field field in StyleVarFields.All)
            {
                vars[field.Id] = field.Get(style);
            }

            return new Preset { Name = name, Colors = colors, Vars = vars };
        }
        finally
        {
            ImGuiNative.ImGuiStyle_destroy(native);
        }
    }
}
