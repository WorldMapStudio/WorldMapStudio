using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Name/value metadata for one enum type, cached — backs the <see cref="CatalogFieldSheet.Enum"/> /
/// <see cref="CatalogFieldSheet.Flags"/> widgets and any catalog's own effect-field pickers built on
/// <see cref="DrawEnumPicker"/>/<see cref="DrawFlagsPicker"/>. The stored column stays a raw <c>int</c>;
/// this only changes how it is shown and picked.
/// </summary>
internal sealed class EnumChoices
{
    private static readonly Dictionary<Type, EnumChoices> Cache = new();

    private EnumChoices(Type enumType)
    {
        IsFlags = enumType.GetCustomAttributes(typeof(FlagsAttribute), false).Length > 0;

        string[] names = System.Enum.GetNames(enumType);
        Array values = System.Enum.GetValues(enumType);
        var byValue = new Dictionary<int, string>();
        var ordered = new List<(int Value, string Name)>();
        for (int i = 0; i < names.Length; i++)
        {
            // int-backed and uint-backed enums both round-trip through the stored int column.
            int value = unchecked((int)Convert.ToInt64(values.GetValue(i)));
            if (byValue.ContainsKey(value))
            {
                continue;
            }

            byValue[value] = names[i];
            ordered.Add((value, names[i]));
        }

        ByValue = byValue;
        Ordered = ordered.OrderBy(entry => unchecked((uint)entry.Value)).ToArray();
    }

    private EnumChoices(IReadOnlyList<(string Label, int Bit)> flagOptions)
    {
        IsFlags = true;

        var byValue = new Dictionary<int, string>();
        var ordered = new List<(int Value, string Name)>();
        foreach ((string label, int bit) in flagOptions)
        {
            if (byValue.ContainsKey(bit))
            {
                continue;
            }

            byValue[bit] = label;
            ordered.Add((bit, label));
        }

        ByValue = byValue;
        Ordered = ordered.OrderBy(entry => unchecked((uint)entry.Value)).ToArray();
    }

    public bool IsFlags { get; }

    public IReadOnlyList<(int Value, string Name)> Ordered { get; }

    public IReadOnlyDictionary<int, string> ByValue { get; }

    public static EnumChoices For(Type enumType)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(enumType, out EnumChoices? choices))
            {
                Cache[enumType] = choices = new EnumChoices(enumType);
            }

            return choices;
        }
    }

    /// <summary>Choices for a flags field whose bits are only known at runtime. Not cached — the caller
    /// owns when the option list changes.</summary>
    public static EnumChoices ForFlags(IReadOnlyList<(string Label, int Bit)> options) => new(options);

    /// <summary>A short human label for <paramref name="value"/> — the member name, or for a flags enum
    /// the set bits joined by <c>|</c> with any leftover as hex.</summary>
    public string Describe(int value)
    {
        if (!IsFlags)
        {
            return ByValue.TryGetValue(value, out string? name) ? name : value.ToString();
        }

        if (value == 0)
        {
            return ByValue.TryGetValue(0, out string? zero) ? zero : "None";
        }

        var hits = new List<string>();
        int rest = value;
        foreach ((int bit, string name) in Ordered)
        {
            if (bit != 0 && (value & bit) == bit)
            {
                hits.Add(name);
                rest &= ~bit;
            }
        }

        if (rest != 0)
        {
            hits.Add($"0x{rest:X}");
        }

        return hits.Count == 0 ? $"0x{value:X}" : string.Join(" | ", hits);
    }
}

/// <summary>
/// Enum / flags picker widgets. The <c>Draw*Picker</c> methods are pure UI — they return the new value
/// on a change and leave recording to the caller, so both the generic field sheet (one
/// <see cref="SetFieldCommand{T}"/>) and a catalog's own bespoke effect list can share them.
/// </summary>
internal static class CatalogEnumField
{
    private static readonly Dictionary<string, string> ComboFilters = new();

    public static void DrawEnum(EditorContext context, CatalogEntity entity, string label, int current,
        Action<int> set, Type enumType)
    {
        if (DrawEnumPicker(label, current, enumType, out int picked))
        {
            Record(context, entity, label, current, picked, set);
        }
    }

    public static void DrawFlags(EditorContext context, CatalogEntity entity, string label, int current,
        Action<int> set, Type enumType)
    {
        if (DrawFlagsPicker(label, current, enumType, out int next))
        {
            Record(context, entity, label, current, next, set);
        }
    }

    public static void DrawFlags(EditorContext context, CatalogEntity entity, string label, int current,
        Action<int> set, IReadOnlyList<(string Label, int Bit)> options)
    {
        if (DrawFlagsPicker(label, current, EnumChoices.ForFlags(options), out int next))
        {
            Record(context, entity, label, current, next, set);
        }
    }

    /// <summary>A searchable dropdown over <paramref name="enumType"/>, with a raw-int box alongside for
    /// values the enum doesn't name. Returns true (and <paramref name="picked"/>) when the value changed.</summary>
    public static bool DrawEnumPicker(string label, int current, Type enumType, out int picked)
    {
        EnumChoices choices = EnumChoices.For(enumType);
        picked = current;
        bool changed = false;

        ImGui.SetNextItemWidth(280.0f);
        if (ImGui.BeginCombo(label, $"{current} · {choices.Describe(current)}"))
        {
            string filter = ComboFilters.TryGetValue(label, out string? f) ? f : string.Empty;
            ImGui.SetNextItemWidth(-1.0f);
            if (ImGui.InputTextWithHint($"##filter_{label}", "filter", ref filter, 64))
            {
                ComboFilters[label] = filter;
            }

            foreach ((int value, string name) in choices.Ordered)
            {
                if (filter.Length > 0 &&
                    name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    !value.ToString().Contains(filter))
                {
                    continue;
                }

                bool selected = value == current;
                if (ImGui.Selectable($"{value} · {name}", selected) && value != current)
                {
                    picked = value;
                    changed = true;
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        int raw = current;
        ImGui.SetNextItemWidth(90.0f);
        if (ImGui.InputInt($"##raw_{label}", ref raw) && raw != current)
        {
            picked = raw;
            changed = true;
        }

        return changed;
    }

    /// <summary>A decoded summary button that opens a checkbox popup, plus a raw-int box. Returns true
    /// (and <paramref name="next"/>) when a bit was toggled or the raw value edited.</summary>
    public static bool DrawFlagsPicker(string label, int current, Type enumType, out int next) =>
        DrawFlagsPicker(label, current, EnumChoices.For(enumType), out next);

    private static bool DrawFlagsPicker(string label, int current, EnumChoices choices, out int next)
    {
        string popupId = $"flags_{label}";
        next = current;
        bool changed = false;

        if (ImGui.Button($"{choices.Describe(current)}##btn_{label}", new System.Numerics.Vector2(280.0f, 0.0f)))
        {
            ImGui.OpenPopup(popupId);
        }

        ImGui.SameLine();
        int raw = current;
        ImGui.SetNextItemWidth(110.0f);
        if (ImGui.InputInt($"{label}##rawflags_{label}", ref raw) && raw != current)
        {
            next = raw;
            changed = true;
        }

        if (ImGui.BeginPopup(popupId))
        {
            foreach ((int bit, string name) in choices.Ordered)
            {
                if (bit == 0)
                {
                    continue;
                }

                bool on = (current & bit) == bit;
                if (ImGui.Checkbox($"{name}  (0x{bit:X})", ref on))
                {
                    next = on ? current | bit : current & ~bit;
                    changed = true;
                }
            }

            ImGui.EndPopup();
        }

        return changed;
    }

    private static void Record(EditorContext context, CatalogEntity entity, string label, int before, int after,
        Action<int> set)
    {
        var command = new SetFieldCommand<int>(entity, label, set, before, after);
        command.Apply();
        context.EditSessions.Record(command);
    }
}
