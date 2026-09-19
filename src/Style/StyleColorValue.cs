using System;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;

namespace WorldMapStudio;

/// <summary>A color as stored in a style file: a literal, or a reference to a palette entry,
/// another ImGui color, or another semantic token — optionally with an alpha/lighten/darken
/// modifier.</summary>
public abstract class StyleColorValue
{
    public static readonly StyleColorValue Missing = new StyleColorLiteral(new Vector4(1f, 0f, 1f, 1f));

    public abstract JsonNode ToJson();

    public static StyleColorValue Parse(JsonNode? node, string path, System.Collections.Generic.List<StyleProblem> problems)
    {
        switch (node)
        {
            case JsonValue value when value.TryGetValue(out string? text) && text is not null:
                return ParseString(text, path, problems);

            case JsonObject obj:
                return ParseObject(obj, path, problems);

            default:
                problems.Add(new StyleProblem(path, $"Unsupported color value '{node}'."));
                return Missing;
        }
    }

    private static StyleColorValue ParseString(string text, string path, System.Collections.Generic.List<StyleProblem> problems)
    {
        if (text.StartsWith('#'))
        {
            if (TryParseHex(text, out Vector4 color))
            {
                return new StyleColorLiteral(color);
            }

            problems.Add(new StyleProblem(path, $"Invalid hex color '{text}'."));
            return Missing;
        }

        if (text.StartsWith('$') || text.StartsWith('@'))
        {
            return new StyleColorReference(text, null, null, null);
        }

        problems.Add(new StyleProblem(path, $"Unrecognized color value '{text}' (expected #hex, $palette, or @ref)."));
        return Missing;
    }

    private static StyleColorValue ParseObject(JsonObject obj, string path, System.Collections.Generic.List<StyleProblem> problems)
    {
        string? target = obj["ref"]?.GetValue<string>();
        if (string.IsNullOrEmpty(target) || (target[0] != '$' && target[0] != '@'))
        {
            problems.Add(new StyleProblem(path, "Color object needs a 'ref' starting with '$' or '@'."));
            return Missing;
        }

        float? alpha = ReadFloat(obj["alpha"]);
        float? lighten = ReadFloat(obj["lighten"]);
        float? darken = ReadFloat(obj["darken"]);
        return new StyleColorReference(target, alpha, lighten, darken);
    }

    private static float? ReadFloat(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out float f) ? f : null;

    /// <summary>Parses a code-declared default (see <see cref="StyleColor"/>) — same grammar as a
    /// file value, but a bad literal here is a programmer error, so this throws instead of reporting
    /// a <see cref="StyleProblem"/>.</summary>
    public static StyleColorValue FromCode(string text)
    {
        if (text.StartsWith('#'))
        {
            if (!TryParseHex(text, out Vector4 color))
            {
                throw new ArgumentException($"Invalid hex color '{text}'.", nameof(text));
            }

            return new StyleColorLiteral(color);
        }

        if (text.StartsWith('$') || text.StartsWith('@'))
        {
            return new StyleColorReference(text, null, null, null);
        }

        throw new ArgumentException($"Invalid color literal '{text}' (expected #hex, $palette, or @ref).", nameof(text));
    }

    public static bool TryParseHex(string text, out Vector4 color)
    {
        color = default;
        string hex = text.TrimStart('#');
        if (hex.Length != 6 && hex.Length != 8)
        {
            return false;
        }

        if (!byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) ||
            !byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) ||
            !byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
        {
            return false;
        }

        byte a = 255;
        if (hex.Length == 8 && !byte.TryParse(hex[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a))
        {
            return false;
        }

        color = new Vector4(r / 255f, g / 255f, b / 255f, a / 255f);
        return true;
    }

    public static string ToHex(Vector4 color)
    {
        static byte Channel(float c) => (byte)Math.Clamp((int)MathF.Round(c * 255f), 0, 255);
        return $"#{Channel(color.X):X2}{Channel(color.Y):X2}{Channel(color.Z):X2}{Channel(color.W):X2}";
    }
}

public sealed class StyleColorLiteral : StyleColorValue
{
    public Vector4 Color { get; }

    public StyleColorLiteral(Vector4 color)
    {
        Color = color;
    }

    public override JsonNode ToJson() => JsonValue.Create(ToHex(Color));
}

/// <summary>A reference to another color, addressed by <paramref name="target"/> ("$name" for a
/// palette entry, "@Name" for an ImGui color or semantic token id).</summary>
public sealed class StyleColorReference : StyleColorValue
{
    public string Target { get; }
    public float? AlphaOverride { get; }
    public float? Lighten { get; }
    public float? Darken { get; }

    public StyleColorReference(string target, float? alphaOverride, float? lighten, float? darken)
    {
        Target = target;
        AlphaOverride = alphaOverride;
        Lighten = lighten;
        Darken = darken;
    }

    public override JsonNode ToJson()
    {
        if (AlphaOverride is null && Lighten is null && Darken is null)
        {
            return JsonValue.Create(Target);
        }

        JsonObject obj = new() { ["ref"] = Target };
        if (AlphaOverride is { } alpha)
        {
            obj["alpha"] = alpha;
        }

        if (Lighten is { } lighten)
        {
            obj["lighten"] = lighten;
        }

        if (Darken is { } darken)
        {
            obj["darken"] = darken;
        }

        return obj;
    }
}
