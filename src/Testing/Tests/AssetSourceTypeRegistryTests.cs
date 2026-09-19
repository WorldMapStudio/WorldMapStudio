using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>Covers <see cref="AssetSourceTypeRegistry"/> discovering its definitions from <see cref="AppSystems"/>.</summary>
public static class AssetSourceTypeRegistryTests
{
    [EditorTest(Category = "Assets", Thread = TestThread.Background)]
    public static void Definitions_are_the_app_root_subsystems_ordered_by_label()
    {
        IReadOnlyList<IAssetSourceDefinition> definitions = AssetSourceTypeRegistry.Definitions;

        Assert.IsTrue(definitions.Any(definition => definition.Type == AssetSourceType.FileSystem), "the built-in filesystem source is registered");
        Assert.IsTrue(
            definitions.SequenceEqual(definitions.OrderBy(definition => definition.Label, StringComparer.OrdinalIgnoreCase)),
            "definitions are ordered by label");
        Assert.AreEqual(
            AppSystems.Instance.Subsystems.OfType<IAssetSourceDefinition>().Count(),
            definitions.Count,
            "every app-root definition is listed");
    }
}
