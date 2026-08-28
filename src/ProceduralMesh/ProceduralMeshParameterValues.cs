using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Godot;

namespace WorldMapStudio;

public sealed class ProceduralMeshParameterValues
{
    private readonly Dictionary<string, string> _values;

    public ProceduralMeshParameterValues()
    {
        _values = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private ProceduralMeshParameterValues(Dictionary<string, string> values)
    {
        _values = values;
    }

    public IReadOnlyDictionary<string, string> Raw => _values;

    public string GetRaw(ProceduralMeshParameter parameter) =>
        _values.TryGetValue(parameter.Name, out string? value) ? value : parameter.Default;

    public float GetFloat(ProceduralMeshParameter parameter) =>
        float.TryParse(GetRaw(parameter), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0.0f;

    public int GetInt(ProceduralMeshParameter parameter) =>
        int.TryParse(GetRaw(parameter), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;

    public bool GetBool(ProceduralMeshParameter parameter) =>
        GetRaw(parameter).Equals("true", StringComparison.OrdinalIgnoreCase);

    public string GetTexture(ProceduralMeshParameter parameter) => GetRaw(parameter);

    public Color GetColor(ProceduralMeshParameter parameter)
    {
        string[] parts = GetRaw(parameter).Split(',');
        if (parts.Length != 4)
        {
            return Colors.White;
        }

        return float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) &&
               float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float g) &&
               float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float b) &&
               float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float a)
            ? new Color(r, g, b, a)
            : Colors.White;
    }

    public void Set(ProceduralMeshParameter parameter, float value) =>
        _values[parameter.Name] = value.ToString(CultureInfo.InvariantCulture);

    public void Set(ProceduralMeshParameter parameter, int value) =>
        _values[parameter.Name] = value.ToString(CultureInfo.InvariantCulture);

    public void Set(ProceduralMeshParameter parameter, bool value) =>
        _values[parameter.Name] = value ? "true" : "false";

    public void Set(ProceduralMeshParameter parameter, string value) =>
        _values[parameter.Name] = value;

    public void Set(ProceduralMeshParameter parameter, Color value) =>
        _values[parameter.Name] = $"{value.R.ToString(CultureInfo.InvariantCulture)},{value.G.ToString(CultureInfo.InvariantCulture)},{value.B.ToString(CultureInfo.InvariantCulture)},{value.A.ToString(CultureInfo.InvariantCulture)}";

    public string Serialize()
    {
        if (_values.Count == 0)
        {
            return "";
        }

        var ordered = new SortedDictionary<string, string>(_values, StringComparer.Ordinal);
        return JsonSerializer.Serialize(ordered);
    }

    public static ProceduralMeshParameterValues Parse(string serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return new ProceduralMeshParameterValues();
        }

        try
        {
            Dictionary<string, string>? parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(serialized);
            return parsed == null
                ? new ProceduralMeshParameterValues()
                : new ProceduralMeshParameterValues(new Dictionary<string, string>(parsed, StringComparer.Ordinal));
        }
        catch (JsonException)
        {
            return new ProceduralMeshParameterValues();
        }
    }
}
