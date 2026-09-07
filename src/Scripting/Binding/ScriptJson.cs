using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace WorldMapStudio;

/// <summary>
/// Converts between the loose values a script hands across the boundary and the typed shapes the
/// editor works in. The script host marshals a JS object into an <see cref="IDictionary{TKey,TValue}"/>
/// and a JS array into an <see cref="IEnumerable"/>, so everything here works off those rather than
/// naming the host's own types.
/// </summary>
public static class ScriptJson
{
    /// <summary>A script value as a settings blob, or null when nothing was passed.</summary>
    public static JsonObject? ToJson(object? value) =>
        AsMap(value) is { } map ? ToJsonObject(map) : null;

    /// <summary>A settings blob as something a script can read field by field.</summary>
    public static IDictionary<string, object?> ToScript(JsonObject value)
    {
        var result = new Dictionary<string, object?>();
        foreach ((string key, JsonNode? node) in value)
        {
            result[key] = FromNode(node);
        }

        return result;
    }

    /// <summary>A script value as a string-keyed map, or null when it is not object-shaped.</summary>
    public static IDictionary<string, object?>? AsMap(object? value) => value switch
    {
        null => null,
        IDictionary<string, object?> typed => typed,
        IDictionary loose => loose.Keys
            .Cast<object>()
            .ToDictionary(key => Convert.ToString(key, CultureInfo.InvariantCulture) ?? "", key => loose[key]),
        _ => null,
    };

    /// <summary>An array of <c>[x, y]</c> pairs as chunk coordinates. Anything else is empty.</summary>
    public static IReadOnlyList<ChunkCoord> ToCoords(object? value)
    {
        if (value is not IEnumerable items || value is string)
        {
            return [];
        }

        var result = new List<ChunkCoord>();
        foreach (object? item in items)
        {
            if (item is not IEnumerable pair || item is string)
            {
                continue;
            }

            int[] parts = pair.Cast<object?>()
                .Take(2)
                .Select(part => Convert.ToInt32(part, CultureInfo.InvariantCulture))
                .ToArray();

            if (parts.Length == 2)
            {
                result.Add(new ChunkCoord(parts[0], parts[1]));
            }
        }

        return result;
    }

    private static JsonObject ToJsonObject(IDictionary<string, object?> map)
    {
        var result = new JsonObject();
        foreach ((string key, object? value) in map)
        {
            result[key] = ToNode(value);
        }

        return result;
    }

    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        string text => JsonValue.Create(text),
        bool flag => JsonValue.Create(flag),
        // Every JS number arrives as a double; narrowing to long when it is whole keeps an id or a
        // count from persisting as "42.0" and reading back as a non-integer.
        double number => JsonValue.Create(number == Math.Floor(number) && !double.IsInfinity(number) ? (long)number : number),
        _ when AsMap(value) is { } nested => ToJsonObject(nested),
        IEnumerable items => new JsonArray(items.Cast<object?>().Select(ToNode).ToArray()),
        _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture)),
    };

    private static object? FromNode(JsonNode? node) => node switch
    {
        null => null,
        JsonObject nested => ToScript(nested),
        JsonArray array => array.Select(FromNode).ToArray(),
        JsonValue value => value.GetValue<object>(),
        _ => null,
    };
}
