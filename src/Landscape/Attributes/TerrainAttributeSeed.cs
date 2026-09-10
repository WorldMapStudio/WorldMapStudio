using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// A terrain attribute an <see cref="ILandscapeProfile"/> declares up front, so a map set up for that
/// export target has the attribute (and its <see cref="TerrainAttributeKind.Enum"/> /
/// <see cref="TerrainAttributeKind.Flags"/> value names) without the user creating it by hand. The
/// created rows carry <see cref="TerrainAttribute.Seeded"/> so a future reseed can update rather than
/// duplicate them.
/// </summary>
public sealed record TerrainAttributeSeed(TerrainAttribute Attribute, IReadOnlyList<TerrainAttributeValueSeed> Values)
{
    public TerrainAttributeSeed(TerrainAttribute attribute)
        : this(attribute, [])
    {
    }
}

/// <summary>One named value or bit of a seeded <see cref="TerrainAttributeSeed"/>.</summary>
public sealed record TerrainAttributeValueSeed(long Value, string Name);
