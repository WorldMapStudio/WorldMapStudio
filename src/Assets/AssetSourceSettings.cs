using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorldMapStudio;

/// <summary>
/// One configured place to look for assets. Kept as a list on the project so users can add more than
/// one source of the same type, for example several filesystem texture folders.
/// </summary>
public sealed class AssetSourceSettings
{
    public string Id { get; set; } = "assets";

    public string Name { get; set; } = "Assets";

    public string Type { get; set; } = AssetSourceType.FileSystem;

    public bool Enabled { get; set; } = true;

    public string RootPath { get; set; } = "";

    public Dictionary<string, string> Properties { get; set; } = new();

    /// <summary>A property from <see cref="Properties"/>, or a top-level string or boolean in the source's JSON.</summary>
    public string GetProperty(string key)
    {
        if (Properties.TryGetValue(key, out string? value))
        {
            return value;
        }

        if (!ExtensionData.TryGetValue(key, out JsonElement element))
        {
            return "";
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        };
    }

    public void SetProperty(string key, string value)
    {
        ExtensionData.Remove(key);
        if (value.Length == 0)
        {
            Properties.Remove(key);
            return;
        }

        Properties[key] = value;
    }

    public bool GetFlag(string key) => GetProperty(key) == "true";

    public void SetFlag(string key, bool value) => SetProperty(key, value ? "true" : "");

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new();
}
