using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WorldMapStudio;

/// <summary>
/// The sparse, in-memory form of one style file: only what it overrides, plus the name of the
/// style it extends. A thin wrapper that preserves unknown top-level keys on round-trip, so a file
/// written by a newer build survives an older one instead of being silently truncated.
/// </summary>
public sealed partial class StyleDocument
{
    public const int CurrentVersion = 1;

    private static readonly string[] KnownTopLevelKeys = ["version", "name", "extends", "palette", "fonts", "vars", "colors", "tokens", "sizes"];

    public int Version { get; set; } = CurrentVersion;
    public string Name { get; set; } = string.Empty;
    public string? Extends { get; set; }

    public Dictionary<string, StyleColorValue> Palette { get; } = new(StringComparer.Ordinal);
    public StyleFontsSection Fonts { get; } = new();
    public Dictionary<string, StyleVarValue> Vars { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, StyleColorValue> Colors { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, StyleColorValue> Tokens { get; } = new(StringComparer.Ordinal);

    /// <summary>Semantic float tokens (<c>StyleSize</c>), e.g. gizmo line thickness. Plain numbers —
    /// unlike colors, sizes don't support the $/@ reference grammar.</summary>
    public Dictionary<string, float> Sizes { get; } = new(StringComparer.Ordinal);

    /// <summary>Problems found while parsing this document alone (not its ancestors) — bad values,
    /// unknown top-level keys. <see cref="StyleResolver"/> folds these into the resolved style's
    /// full problem list.</summary>
    public List<StyleProblem> Problems { get; } = new();

    private JsonObject? _unknownTopLevel;

    public static StyleDocument Parse(string json)
    {
        StyleDocument document = new();
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            document.Problems.Add(new StyleProblem("$", $"Invalid JSON: {ex.Message}"));
            return document;
        }

        if (root is not JsonObject obj)
        {
            document.Problems.Add(new StyleProblem("$", "Style file must be a JSON object."));
            return document;
        }

        document.Version = obj["version"] is JsonValue v && v.TryGetValue(out int version) ? version : CurrentVersion;
        document.Name = obj["name"]?.GetValue<string>() ?? string.Empty;
        document.Extends = obj["extends"]?.GetValue<string>();

        if (obj["palette"] is JsonObject palette)
        {
            foreach ((string key, JsonNode? value) in palette)
            {
                document.Palette[key] = StyleColorValue.Parse(value, $"palette.{key}", document.Problems);
            }
        }

        if (obj["fonts"] is JsonObject fonts)
        {
            document.Fonts.Scale = fonts["scale"] is JsonValue s && s.TryGetValue(out float scale) ? scale : null;
            foreach ((string key, JsonNode? value) in fonts)
            {
                if (key == "scale")
                {
                    continue;
                }

                if (value is JsonObject slotObj)
                {
                    document.Fonts.Slots[key] = StyleFontSlotValue.Parse(slotObj, $"fonts.{key}", document.Problems);
                }
                else
                {
                    document.Problems.Add(new StyleProblem($"fonts.{key}", "Expected a font slot object."));
                }
            }
        }

        if (obj["vars"] is JsonObject vars)
        {
            foreach ((string key, JsonNode? value) in vars)
            {
                document.Vars[key] = StyleVarValue.Parse(value, $"vars.{key}", document.Problems);
            }
        }

        if (obj["colors"] is JsonObject colors)
        {
            foreach ((string key, JsonNode? value) in colors)
            {
                document.Colors[key] = StyleColorValue.Parse(value, $"colors.{key}", document.Problems);
            }
        }

        if (obj["tokens"] is JsonObject tokens)
        {
            foreach ((string key, JsonNode? value) in tokens)
            {
                document.Tokens[key] = StyleColorValue.Parse(value, $"tokens.{key}", document.Problems);
            }
        }

        if (obj["sizes"] is JsonObject sizes)
        {
            foreach ((string key, JsonNode? value) in sizes)
            {
                if (value is JsonValue sv && sv.TryGetValue(out float f))
                {
                    document.Sizes[key] = f;
                }
                else
                {
                    document.Problems.Add(new StyleProblem($"sizes.{key}", $"Expected a number, got '{value}'."));
                }
            }
        }

        foreach ((string key, JsonNode? value) in obj)
        {
            if (Array.IndexOf(KnownTopLevelKeys, key) < 0)
            {
                document._unknownTopLevel ??= new JsonObject();
                document._unknownTopLevel[key] = value?.DeepClone();
                document.Problems.Add(new StyleProblem(key, $"Unknown key '{key}' kept as-is."));
            }
        }

        return document;
    }

    public string ToJson()
    {
        JsonObject obj = _unknownTopLevel is not null ? (JsonObject)_unknownTopLevel.DeepClone() : new JsonObject();
        obj["version"] = Version;
        obj["name"] = Name;
        if (Extends is not null)
        {
            obj["extends"] = Extends;
        }

        if (Palette.Count > 0)
        {
            JsonObject palette = new();
            foreach ((string key, StyleColorValue value) in Palette.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                palette[key] = value.ToJson();
            }

            obj["palette"] = palette;
        }

        if (Fonts.Scale is not null || Fonts.Slots.Count > 0)
        {
            JsonObject fonts = new();
            if (Fonts.Scale is { } scale)
            {
                fonts["scale"] = scale;
            }

            foreach ((string key, StyleFontSlotValue value) in Fonts.Slots.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                fonts[key] = value.ToJson();
            }

            obj["fonts"] = fonts;
        }

        if (Vars.Count > 0)
        {
            JsonObject vars = new();
            foreach ((string key, StyleVarValue value) in Vars.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                vars[key] = value.ToJson();
            }

            obj["vars"] = vars;
        }

        if (Colors.Count > 0)
        {
            JsonObject colors = new();
            foreach ((string key, StyleColorValue value) in Colors.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                colors[key] = value.ToJson();
            }

            obj["colors"] = colors;
        }

        if (Tokens.Count > 0)
        {
            JsonObject tokens = new();
            foreach ((string key, StyleColorValue value) in Tokens.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                tokens[key] = value.ToJson();
            }

            obj["tokens"] = tokens;
        }

        if (Sizes.Count > 0)
        {
            JsonObject sizes = new();
            foreach ((string key, float value) in Sizes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                sizes[key] = value;
            }

            obj["sizes"] = sizes;
        }

        return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public StyleDocument Clone() => Parse(ToJson());
}
