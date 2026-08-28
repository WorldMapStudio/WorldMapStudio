using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;

namespace WorldMapStudio;

public static class AssetSourceTypeRegistry
{
    private static readonly Lazy<IReadOnlyList<IAssetSourceDefinition>> LazyDefinitions = new(Discover);

    public static IReadOnlyList<IAssetSourceDefinition> Definitions => LazyDefinitions.Value;

    public static IAssetSourceDefinition? Find(string type) =>
        Definitions.FirstOrDefault(definition => definition.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

    public static string Label(string type) => Find(type)?.Label ?? type;

    private static IReadOnlyList<IAssetSourceDefinition> Discover()
    {
        var definitions = new List<IAssetSourceDefinition>();
        foreach (Type type in SafeGetTypes(typeof(AssetSourceTypeRegistry).Assembly))
        {
            if (type.IsAbstract || !typeof(IAssetSourceDefinition).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null)
            {
                continue;
            }

            try
            {
                definitions.Add((IAssetSourceDefinition)Activator.CreateInstance(type)!);
            }
            catch (Exception e)
            {
                GD.PushError($"[Assets] Failed to register asset source type '{type.FullName}': {e.Message}");
            }
        }

        return definitions
            .GroupBy(definition => definition.Type, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(definition => definition.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(type => type != null)!;
        }
    }
}
