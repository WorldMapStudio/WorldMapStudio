using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Resolves a raw terrain-attribute value to a readable label using the attribute's own declaration:
/// an <see cref="TerrainAttributeKind.Enum"/> case name, the space-joined names of the set bits of a
/// <see cref="TerrainAttributeKind.Flags"/> value, or the number otherwise. Shared by the debug
/// readout and the <c>attributes</c> script module so both name a value the same way.
/// </summary>
public static class TerrainAttributeNames
{
    public static string Resolve(TerrainAttribute attribute, long value, IEnumerable<TerrainAttributeValue> allValues)
    {
        int attributeId = attribute.RecordId ?? -1;

        switch (attribute.Kind)
        {
            case TerrainAttributeKind.Enum:
                return allValues.FirstOrDefault(row => row.AttributeId == attributeId && row.Value == value)?.Name
                    ?? value.ToString();

            case TerrainAttributeKind.Flags:
            {
                string[] names = allValues
                    .Where(row => row.AttributeId == attributeId && row.Value != 0 && (value & row.Value) == row.Value)
                    .Select(row => row.Name)
                    .ToArray();
                return names.Length > 0 ? string.Join(" ", names) : $"0x{value:X}";
            }

            default:
                return value.ToString();
        }
    }
}
