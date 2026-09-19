using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>A style available to activate, from <see cref="StyleScriptApi.List"/>.</summary>
public sealed class StyleListEntry
{
    public StyleListEntry(string name, string source, string? extends, bool readOnly)
    {
        Name = name;
        Source = source;
        Extends = extends;
        ReadOnly = readOnly;
    }

    [ScriptProperty] public string Name { get; }
    [ScriptProperty] public string Source { get; }
    [ScriptProperty] public string? Extends { get; }
    [ScriptProperty] public bool ReadOnly { get; }
}

/// <summary>One row of <see cref="StyleScriptApi.Tokens"/> — an ImGui color, a numeric var, or a
/// semantic <see cref="StyleColor"/>/<see cref="StyleSize"/> token.</summary>
public sealed class StyleTokenDescriptor
{
    public StyleTokenDescriptor(string id, string group, string kind, string resolved, string? raw, bool overridden)
    {
        Id = id;
        Group = group;
        Kind = kind;
        Resolved = resolved;
        Raw = raw;
        Overridden = overridden;
    }

    [ScriptProperty] public string Id { get; }
    [ScriptProperty] public string Group { get; }
    [ScriptProperty] public string Kind { get; }
    [ScriptProperty] public string Resolved { get; }
    [ScriptProperty] public string? Raw { get; }
    [ScriptProperty] public bool Overridden { get; }
}

public sealed class StyleProblemDescriptor
{
    public StyleProblemDescriptor(string path, string message)
    {
        Path = path;
        Message = message;
    }

    [ScriptProperty] public string Path { get; }
    [ScriptProperty] public string Message { get; }
}

public sealed class SystemFontDescriptor
{
    public SystemFontDescriptor(string family, bool supported)
    {
        Family = family;
        Supported = supported;
    }

    [ScriptProperty] public string Family { get; }
    [ScriptProperty] public bool Supported { get; }
}

public sealed class FontVariantDescriptor
{
    public FontVariantDescriptor(int weight, bool italic, string path, int faceIndex)
    {
        Weight = weight;
        Italic = italic;
        Path = path;
        FaceIndex = faceIndex;
    }

    [ScriptProperty] public int Weight { get; }
    [ScriptProperty] public bool Italic { get; }
    [ScriptProperty] public string Path { get; }
    [ScriptProperty] public int FaceIndex { get; }
}

/// <summary>
/// Everything the Style Editor window does, exposed to JS as <c>wms.style</c> — lets a script (or an
/// LLM driving one) build a whole theme end-to-end: <see cref="Create"/>, a handful of
/// <see cref="SetPalette"/>/<see cref="Set"/> calls, <see cref="Save"/>.
///
/// Ids are unified across kinds: an ImGui color is <c>"imgui.Button"</c>, a numeric <c>ImGuiStyle</c>
/// field is <c>"var.FrameRounding"</c>, a font value is <c>"font.scale"</c> or <c>"font.ui.size"</c>,
/// a palette entry is <c>"$accent"</c>, and a semantic token is addressed by its own id
/// (<c>"text.error"</c>). <see cref="Get"/>/<see cref="Set"/> take that unified id; both throw
/// <see cref="InvalidOperationException"/> on an id that doesn't resolve to anything, the same as
/// <see cref="ViewScriptApi"/>'s <c>Resolve</c>.
/// </summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class StyleScriptApi : IScriptModule
{
    private enum IdKind
    {
        ImGuiColor,
        Var,
        FontScale,
        FontField,
        Palette,
        Token,
    }

    public string Name => "style";

    public StyleScriptApi(ScriptingSystem system)
    {
    }

    [ScriptFunction]
    public StyleListEntry[] List() => EditorStyle.Available()
        .Select(static d => new StyleListEntry(d.Name, d.Source.ToString().ToLowerInvariant(), d.Extends, d.ReadOnly))
        .ToArray();

    [ScriptProperty]
    public string Active => EditorStyle.ActiveName;

    [ScriptFunction]
    public void Activate(string name) => EditorStyle.Activate(name);

    [ScriptFunction]
    public void Create(string name, string extends = "Dark")
    {
        if (!EditorStyle.Create(name, extends, out string? error))
        {
            throw new InvalidOperationException(error);
        }
    }

    [ScriptFunction]
    public void Duplicate(string name, string newName)
    {
        if (!EditorStyle.Duplicate(name, newName, out string? error))
        {
            throw new InvalidOperationException(error);
        }
    }

    [ScriptFunction]
    public void Rename(string name, string newName)
    {
        if (!EditorStyle.Rename(name, newName, out string? error))
        {
            throw new InvalidOperationException(error);
        }
    }

    [ScriptFunction]
    public void Delete(string name)
    {
        if (!EditorStyle.Delete(name, out string? error))
        {
            throw new InvalidOperationException(error);
        }
    }

    /// <summary>Every ImGui color, numeric var, and semantic token — what the Style Editor's
    /// Interface/Sizes/Editor Colors tabs show, combined into one list.</summary>
    [ScriptFunction]
    public StyleTokenDescriptor[] Tokens()
    {
        List<StyleTokenDescriptor> list = [];

        foreach (ImGuiCol col in Enum.GetValues<ImGuiCol>())
        {
            if (col == ImGuiCol.COUNT)
            {
                continue;
            }

            string name = col.ToString();
            bool overridden = EditorStyle.Working.Colors.TryGetValue(name, out StyleColorValue? raw);
            list.Add(new StyleTokenDescriptor($"imgui.{name}", "ImGui", "imgui",
                StyleColorValue.ToHex(EditorStyle.Active.Colors[col]), overridden ? RawColorText(raw!) : null, overridden));
        }

        foreach (StyleVarFields.Field field in StyleVarFields.All)
        {
            bool overridden = EditorStyle.Working.Vars.TryGetValue(field.Id, out StyleVarValue raw);
            list.Add(new StyleTokenDescriptor($"var.{field.Id}", "Vars", "var",
                VarText(EditorStyle.Active.Vars[field.Id]), overridden ? VarText(raw) : null, overridden));
        }

        foreach (StyleColor token in StyleTokenRegistry.Colors)
        {
            bool overridden = EditorStyle.Working.Tokens.TryGetValue(token.Id, out StyleColorValue? raw);
            list.Add(new StyleTokenDescriptor(token.Id, token.Group, "semantic",
                StyleColorValue.ToHex(token.Value), overridden ? RawColorText(raw!) : null, overridden));
        }

        foreach (StyleSize size in StyleTokenRegistry.Sizes)
        {
            bool overridden = EditorStyle.Working.Sizes.TryGetValue(size.Id, out float raw);
            list.Add(new StyleTokenDescriptor(size.Id, size.Group, "semantic",
                size.Value.ToString("R"), overridden ? raw.ToString("R") : null, overridden));
        }

        return list.ToArray();
    }

    /// <summary>Resolved value as <c>"#RRGGBBAA"</c> for a color id, or a <c>number</c>/<c>[x, y]</c>
    /// array for a var id.</summary>
    [ScriptFunction]
    public object Get(string id)
    {
        (IdKind kind, string key, string? slot, string? field) = ParseId(id);
        return kind switch
        {
            IdKind.ImGuiColor => StyleColorValue.ToHex(RequireImGuiColor(key, id)),
            IdKind.Palette => StyleColorValue.ToHex(RequirePalette(key, id)),
            IdKind.Var => VarToScript(RequireVar(key, id)),
            IdKind.FontScale => EditorStyle.Active.FontScale,
            IdKind.FontField => GetFontField(slot!, field!, id),
            IdKind.Token => GetToken(key, id),
            _ => throw new InvalidOperationException($"Unknown style id '{id}'."),
        };
    }

    /// <summary>Same value grammar as a style file: a hex/$/@ string for a color id, a number or
    /// <c>[x, y]</c> for a var id. Applies live and marks the active style dirty.</summary>
    [ScriptFunction]
    public void Set(string id, object value)
    {
        (IdKind kind, string key, string? slot, string? field) = ParseId(id);
        switch (kind)
        {
            case IdKind.ImGuiColor:
                RequireImGuiColorId(key, id);
                EditorStyle.Working.Colors[key] = ParseScriptColor(value, id);
                break;
            case IdKind.Palette:
                EditorStyle.Working.Palette[key] = ParseScriptColor(value, id);
                break;
            case IdKind.Var:
                RequireVarId(key, id);
                EditorStyle.Working.Vars[key] = ParseScriptVar(value, id);
                break;
            case IdKind.FontScale:
                EditorStyle.Working.Fonts.Scale = ToFloat(value, id);
                break;
            case IdKind.FontField:
                SetFontField(slot!, field!, value, id);
                break;
            case IdKind.Token:
                SetToken(key, value, id);
                break;
            default:
                throw new InvalidOperationException($"Unknown style id '{id}'.");
        }

        EditorStyle.NotifyWorkingChanged();
    }

    [ScriptFunction]
    public void Reset(string id)
    {
        (IdKind kind, string key, string? slot, _) = ParseId(id);
        switch (kind)
        {
            case IdKind.ImGuiColor: EditorStyle.Working.Colors.Remove(key); break;
            case IdKind.Palette: EditorStyle.Working.Palette.Remove(key); break;
            case IdKind.Var: EditorStyle.Working.Vars.Remove(key); break;
            case IdKind.FontScale: EditorStyle.Working.Fonts.Scale = null; break;
            case IdKind.FontField: EditorStyle.Working.Fonts.Slots.Remove(slot!); break;
            case IdKind.Token:
                if (!EditorStyle.Working.Tokens.Remove(key))
                {
                    EditorStyle.Working.Sizes.Remove(key);
                }

                break;
        }

        EditorStyle.NotifyWorkingChanged();
    }

    [ScriptFunction]
    public void SetPalette(string name, string value)
    {
        EditorStyle.Working.Palette[name] = ParseScriptColor(value, $"${name}");
        EditorStyle.NotifyWorkingChanged();
    }

    [ScriptFunction]
    public void RemovePalette(string name)
    {
        EditorStyle.Working.Palette.Remove(name);
        EditorStyle.NotifyWorkingChanged();
    }

    [ScriptProperty]
    public bool Dirty => EditorStyle.IsWorkingDirty;

    [ScriptFunction]
    public void Save()
    {
        if (!EditorStyle.Save(out string? error))
        {
            throw new InvalidOperationException(error);
        }
    }

    [ScriptFunction]
    public void SaveAs(string name)
    {
        if (!EditorStyle.SaveAs(name, out string? error))
        {
            throw new InvalidOperationException(error);
        }
    }

    [ScriptFunction]
    public void Revert() => EditorStyle.Revert();

    [ScriptFunction]
    public StyleProblemDescriptor[] Problems() =>
        EditorStyle.Problems.Select(static p => new StyleProblemDescriptor(p.Path, p.Message)).ToArray();

    /// <summary>The system font families, optionally narrowed by a name filter. Resolves once the first
    /// scan this session has finished.</summary>
    [ScriptFunction]
    public async Task<SystemFontDescriptor[]> SystemFonts(string? filter = null)
    {
        IEnumerable<SystemFontFamilyInfo> families = await SystemFontCatalog.ScanAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(filter))
        {
            families = families.Where(f => f.Family.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        return families.Select(static f => new SystemFontDescriptor(f.Family, f.Supported)).ToArray();
    }

    [ScriptFunction]
    public FontVariantDescriptor[] FontVariants(string family) => SystemFontCatalog.Variants(family)
        .Select(static v => new FontVariantDescriptor(v.Weight, v.Italic, v.Path, v.FaceIndex))
        .ToArray();

    /// <summary>Sets several of a font slot's fields at once. Unpassed (<c>null</c>) fields keep
    /// their current value; passing <paramref name="family"/> or <paramref name="file"/> clears the
    /// other source's fields, same as switching Source in the window.</summary>
    [ScriptFunction]
    public void SetFont(string slot, string? family = null, int? weight = null, bool? italic = null, string? file = null, float? size = null)
    {
        if (StyleTokenRegistry.Fonts.All(f => f.Id != slot))
        {
            throw new InvalidOperationException($"Unknown font slot '{slot}'.");
        }

        StyleFontSlotValue current = EditorStyle.Working.Fonts.Slots.GetValueOrDefault(slot) ?? new StyleFontSlotValue();
        if (file is not null)
        {
            current = current with { File = file, Family = null, Weight = null, Italic = null };
        }
        else if (family is not null)
        {
            current = current with { Family = family, File = null, Weight = weight ?? current.Weight, Italic = italic ?? current.Italic };
        }
        else
        {
            current = current with { Weight = weight ?? current.Weight, Italic = italic ?? current.Italic };
        }

        if (size is not null)
        {
            current = current with { Size = size };
        }

        EditorStyle.Working.Fonts.Slots[slot] = current;
        EditorStyle.NotifyWorkingChanged();
    }

    // ---- Id grammar --------------------------------------------------------------------------

    private static (IdKind Kind, string Key, string? Slot, string? Field) ParseId(string id)
    {
        if (id.StartsWith("imgui.", StringComparison.Ordinal))
        {
            return (IdKind.ImGuiColor, id["imgui.".Length..], null, null);
        }

        if (id.StartsWith("var.", StringComparison.Ordinal))
        {
            return (IdKind.Var, id["var.".Length..], null, null);
        }

        if (id == "font.scale")
        {
            return (IdKind.FontScale, id, null, null);
        }

        if (id.StartsWith("font.", StringComparison.Ordinal))
        {
            string rest = id["font.".Length..];
            int dot = rest.IndexOf('.');
            if (dot < 0)
            {
                throw new InvalidOperationException($"Malformed font id '{id}' (expected 'font.<slot>.<field>').");
            }

            return (IdKind.FontField, rest, rest[..dot], rest[(dot + 1)..]);
        }

        if (id.StartsWith("$", StringComparison.Ordinal))
        {
            return (IdKind.Palette, id[1..], null, null);
        }

        return (IdKind.Token, id, null, null);
    }

    private static Vector4 RequireImGuiColor(string name, string fullId)
    {
        RequireImGuiColorId(name, fullId);
        return EditorStyle.Active.Colors[Enum.Parse<ImGuiCol>(name)];
    }

    private static void RequireImGuiColorId(string name, string fullId)
    {
        if (!Enum.TryParse(name, out ImGuiCol col) || col == ImGuiCol.COUNT)
        {
            throw new InvalidOperationException($"Unknown style id '{fullId}'.");
        }
    }

    private static Vector4 RequirePalette(string name, string fullId) =>
        EditorStyle.Active.Palette.TryGetValue(name, out Vector4 v) ? v : throw new InvalidOperationException($"Unknown style id '{fullId}'.");

    private static StyleVarValue RequireVar(string name, string fullId) =>
        EditorStyle.Active.Vars.TryGetValue(name, out StyleVarValue v) ? v : throw new InvalidOperationException($"Unknown style id '{fullId}'.");

    private static void RequireVarId(string name, string fullId)
    {
        if (StyleVarFields.Find(name) is null)
        {
            throw new InvalidOperationException($"Unknown style id '{fullId}'.");
        }
    }

    private static object GetToken(string id, string fullId)
    {
        if (StyleTokenRegistry.Colors.Any(c => c.Id == id))
        {
            return StyleColorValue.ToHex(EditorStyle.Active.Tokens.TryGetValue(id, out Vector4 v) ? v : throw new InvalidOperationException($"Unknown style id '{fullId}'."));
        }

        if (StyleTokenRegistry.Sizes.Any(s => s.Id == id))
        {
            return EditorStyle.Active.Sizes.TryGetValue(id, out float f) ? f : throw new InvalidOperationException($"Unknown style id '{fullId}'.");
        }

        throw new InvalidOperationException($"Unknown style id '{fullId}'.");
    }

    private static void SetToken(string id, object value, string fullId)
    {
        if (StyleTokenRegistry.Colors.Any(c => c.Id == id))
        {
            EditorStyle.Working.Tokens[id] = ParseScriptColor(value, fullId);
            return;
        }

        if (StyleTokenRegistry.Sizes.Any(s => s.Id == id))
        {
            EditorStyle.Working.Sizes[id] = ToFloat(value, fullId);
            return;
        }

        throw new InvalidOperationException($"Unknown style id '{fullId}'.");
    }

    private static object GetFontField(string slotId, string field, string fullId)
    {
        StyleFont? declared = StyleTokenRegistry.Fonts.FirstOrDefault(f => f.Id == slotId);
        if (declared is null)
        {
            throw new InvalidOperationException($"Unknown font slot '{slotId}'.");
        }

        EditorStyle.Active.FontSlots.TryGetValue(slotId, out StyleFontSlotValue? slot);
        return field switch
        {
            "family" => (object?)slot?.Family ?? string.Empty,
            "weight" => slot?.Weight ?? 400,
            "italic" => slot?.Italic ?? false,
            "file" => (object?)slot?.File ?? string.Empty,
            "size" => slot?.Size ?? declared.DefaultSize,
            _ => throw new InvalidOperationException($"Unknown font field '{fullId}'."),
        };
    }

    private static void SetFontField(string slotId, string field, object value, string fullId)
    {
        if (StyleTokenRegistry.Fonts.All(f => f.Id != slotId))
        {
            throw new InvalidOperationException($"Unknown font slot '{slotId}'.");
        }

        StyleFontSlotValue current = EditorStyle.Working.Fonts.Slots.GetValueOrDefault(slotId) ?? new StyleFontSlotValue();
        EditorStyle.Working.Fonts.Slots[slotId] = field switch
        {
            "family" => current with { Family = Convert.ToString(value), File = null },
            "weight" => current with { Weight = Convert.ToInt32(value) },
            "italic" => current with { Italic = Convert.ToBoolean(value) },
            "file" => current with { File = Convert.ToString(value), Family = null, Weight = null, Italic = null },
            "size" => current with { Size = ToFloat(value, fullId) },
            _ => throw new InvalidOperationException($"Unknown font field '{fullId}'."),
        };
    }

    private static StyleColorValue ParseScriptColor(object value, string id)
    {
        if (value is not string text)
        {
            throw new InvalidOperationException($"Expected a color string for '{id}'.");
        }

        try
        {
            return StyleColorValue.FromCode(text);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"Invalid color value for '{id}': {ex.Message}");
        }
    }

    private static StyleVarValue ParseScriptVar(object value, string id)
    {
        if (value is System.Collections.IEnumerable list and not string)
        {
            object[] items = list.Cast<object>().ToArray();
            if (items.Length != 2)
            {
                throw new InvalidOperationException($"Expected [x, y] for '{id}'.");
            }

            return StyleVarValue.Vector(ToFloat(items[0], id), ToFloat(items[1], id));
        }

        return StyleVarValue.Scalar(ToFloat(value, id));
    }

    private static float ToFloat(object value, string id) => value switch
    {
        double d => (float)d,
        float f => f,
        int i => i,
        long l => l,
        _ => throw new InvalidOperationException($"Expected a number for '{id}'."),
    };

    private static string RawColorText(StyleColorValue value) => value switch
    {
        StyleColorLiteral literal => StyleColorValue.ToHex(literal.Color),
        StyleColorReference reference => reference.Target,
        _ => "?",
    };

    private static object VarToScript(StyleVarValue value) =>
        value.IsVector ? new object[] { value.X, value.Y } : value.AsFloat();

    private static string VarText(StyleVarValue value) =>
        value.IsVector ? $"[{value.X:R}, {value.Y:R}]" : value.AsFloat().ToString("R");
}
