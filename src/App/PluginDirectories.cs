using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace WorldMapStudio;

/// <summary>Locates a plugin's checkout directory from the plugins root recorded at build time.</summary>
public static class PluginDirectories
{
    private const string MetadataKey = "WorldMapStudio.PluginsDirectory";

    /// <summary>The plugin's directory, or null when it does not exist. Never guesses.</summary>
    public static string? Find(string pluginName)
    {
        string? root = typeof(PluginDirectories).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == MetadataKey)?.Value;
        if (string.IsNullOrEmpty(root))
        {
            return null;
        }

        string candidate = Path.Combine(root, pluginName);
        return Directory.Exists(candidate) ? candidate : null;
    }
}
