using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// The values a material supplies for one function's parameters, keyed by
/// <see cref="LandscapeParameter.Name"/>.
///
/// Values are held as strings and parsed on read, for one reason that matters: a value whose
/// parameter the function no longer declares is <em>kept</em> rather than dropped. Editing a material
/// against a newer version of a function, or against a function whose plugin is not loaded right now,
/// does not silently destroy what was authored.
/// </summary>
public sealed class LandscapeParameterValues
{
    private readonly Dictionary<string, string> _values;

    public LandscapeParameterValues()
    {
        _values = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private LandscapeParameterValues(Dictionary<string, string> values)
    {
        _values = values;
    }

    public IReadOnlyDictionary<string, string> Raw => _values;

    /// <summary>Reads the raw string for a parameter, falling back to its declared default.</summary>
    public string GetRaw(LandscapeParameter parameter) =>
        _values.TryGetValue(parameter.Name, out string? value) ? value : parameter.Default;

    public float GetFloat(LandscapeParameter parameter) =>
        float.TryParse(GetRaw(parameter), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0.0f;

    public int GetInt(LandscapeParameter parameter) =>
        int.TryParse(GetRaw(parameter), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;

    /// <summary>Reads a parameter as an unsigned 32-bit value — the stored form of a
    /// <see cref="LandscapeParameterKind.AttributeValue"/>, which may name a bit above <c>int.MaxValue</c>.</summary>
    public uint GetUInt(LandscapeParameter parameter) =>
        long.TryParse(GetRaw(parameter), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? unchecked((uint)value)
            : 0u;

    public bool GetBool(LandscapeParameter parameter) =>
        GetRaw(parameter).Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>The channel name bound to a channel parameter, or empty when unbound. The raw stored
    /// value may carry a <c>:swizzle</c> suffix (see <see cref="LandscapeChannelBinding"/>) — this
    /// strips it, for callers that only need the channel's identity, e.g. to check it still exists.</summary>
    public string GetChannel(LandscapeParameter parameter) => GetChannelBinding(parameter).Channel;

    /// <summary>The channel and which of its components a channel parameter reads or writes.</summary>
    public LandscapeChannelBinding GetChannelBinding(LandscapeParameter parameter) =>
        LandscapeChannelBinding.Parse(GetRaw(parameter));

    /// <summary>Parses the "r,g,b,a" a <see cref="Set(LandscapeParameter, Color)"/> writes, falling back
    /// to white on anything unreadable — an empty or hand-edited value should not crash a build.</summary>
    public Color GetColor(LandscapeParameter parameter)
    {
        string[] parts = GetRaw(parameter).Split(',');
        if (parts.Length != 4 ||
            !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float g) ||
            !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float b) ||
            !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float a))
        {
            return Colors.White;
        }

        return new Color(r, g, b, a);
    }

    public void Set(LandscapeParameter parameter, Color value) =>
        _values[parameter.Name] = string.Join(",",
            value.R.ToString(CultureInfo.InvariantCulture),
            value.G.ToString(CultureInfo.InvariantCulture),
            value.B.ToString(CultureInfo.InvariantCulture),
            value.A.ToString(CultureInfo.InvariantCulture));

    public void Set(LandscapeParameter parameter, float value) =>
        _values[parameter.Name] = value.ToString(CultureInfo.InvariantCulture);

    public void Set(LandscapeParameter parameter, int value) =>
        _values[parameter.Name] = value.ToString(CultureInfo.InvariantCulture);

    public void Set(LandscapeParameter parameter, bool value) =>
        _values[parameter.Name] = value ? "true" : "false";

    public void Set(LandscapeParameter parameter, string value) =>
        _values[parameter.Name] = value;

    /// <summary>Serializes to the string a material stores. Stable ordering, so commits stay diffable.</summary>
    public string Serialize()
    {
        if (_values.Count == 0)
        {
            return "";
        }

        var ordered = new SortedDictionary<string, string>(_values, StringComparer.Ordinal);
        return JsonSerializer.Serialize(ordered);
    }

    /// <summary>Reads back what <see cref="Serialize"/> wrote. Unreadable input yields empty values.</summary>
    public static LandscapeParameterValues Parse(string serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return new LandscapeParameterValues();
        }

        try
        {
            Dictionary<string, string>? parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(serialized);
            return parsed == null
                ? new LandscapeParameterValues()
                : new LandscapeParameterValues(new Dictionary<string, string>(parsed, StringComparer.Ordinal));
        }
        catch (JsonException)
        {
            // Hand-edited or written by an older format: better an empty bag than a crash while drawing.
            return new LandscapeParameterValues();
        }
    }
}
