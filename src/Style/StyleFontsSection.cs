using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace WorldMapStudio;

/// <summary>One font slot's setting, e.g. the "ui" or "monospace" entry under a style's
/// <c>"fonts"</c> object. <see cref="File"/> and <see cref="Family"/> are mutually exclusive modes;
/// neither set means "use ImGui's built-in default font".</summary>
public sealed class StyleFontSlotValue
{
    public string? Family { get; init; }
    public int? Weight { get; init; }
    public bool? Italic { get; init; }
    public string? File { get; init; }
    public float? Size { get; init; }

    /// <summary>Keys this build doesn't recognize, kept so a newer build's fields survive a round trip.</summary>
    public JsonObject? Unknown { get; init; }

    private static readonly string[] KnownKeys = ["family", "weight", "italic", "file", "size"];

    public static StyleFontSlotValue Parse(JsonObject obj, string path, List<StyleProblem> problems)
    {
        string? family = obj["family"]?.GetValue<string>();
        int? weight = obj["weight"] is JsonValue w && w.TryGetValue(out int wv) ? wv : null;
        bool? italic = obj["italic"] is JsonValue i && i.TryGetValue(out bool iv) ? iv : null;
        string? file = obj["file"]?.GetValue<string>();
        float? size = obj["size"] is JsonValue s && s.TryGetValue(out float sv) ? sv : null;

        if (family is not null && file is not null)
        {
            problems.Add(new StyleProblem(path, "Font slot sets both 'family' and 'file'; 'file' wins."));
            family = null;
            weight = null;
            italic = null;
        }

        JsonObject? unknown = null;
        foreach ((string key, JsonNode? value) in obj)
        {
            if (System.Array.IndexOf(KnownKeys, key) < 0)
            {
                unknown ??= new JsonObject();
                unknown[key] = value?.DeepClone();
            }
        }

        return new StyleFontSlotValue { Family = family, Weight = weight, Italic = italic, File = file, Size = size, Unknown = unknown };
    }

    public JsonObject ToJson()
    {
        JsonObject obj = Unknown is not null ? (JsonObject)Unknown.DeepClone() : new JsonObject();
        if (Family is not null) obj["family"] = Family;
        if (Weight is not null) obj["weight"] = Weight;
        if (Italic is not null) obj["italic"] = Italic;
        if (File is not null) obj["file"] = File;
        if (Size is not null) obj["size"] = Size;
        return obj;
    }
}

/// <summary>The sparse <c>"fonts"</c> section of a style file: the global scale plus per-slot settings.</summary>
public sealed class StyleFontsSection
{
    public float? Scale { get; set; }
    public Dictionary<string, StyleFontSlotValue> Slots { get; } = new(System.StringComparer.Ordinal);
}
