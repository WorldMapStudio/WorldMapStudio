using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

public static class AssetSourceTypeRegistry
{
    private static readonly Lazy<IReadOnlyList<IAssetSourceDefinition>> LazyDefinitions = new(Discover);

    public static IReadOnlyList<IAssetSourceDefinition> Definitions => LazyDefinitions.Value;

    public static IAssetSourceDefinition? Find(string type) =>
        Definitions.FirstOrDefault(definition => definition.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

    public static string Label(string type) => Find(type)?.Label ?? type;

    private static IReadOnlyList<IAssetSourceDefinition> Discover() =>
        AppSystems.Instance.Subsystems
            .OfType<IAssetSourceDefinition>()
            .OrderBy(definition => definition.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
