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

    public string GetProperty(string key) =>
        Properties.TryGetValue(key, out string? value) ? value :
        ExtensionData.TryGetValue(key, out JsonElement element) && element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" :
        "";

    public void SetProperty(string key, string value)
    {
        if (value.Length == 0)
        {
            Properties.Remove(key);
            return;
        }

        Properties[key] = value;
    }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new();
}
