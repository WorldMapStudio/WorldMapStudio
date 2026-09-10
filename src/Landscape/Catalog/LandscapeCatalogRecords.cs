namespace WorldMapStudio;

/// <summary>EF Core row backing a <see cref="LandscapeChannel"/> in the Editor storage.</summary>
public sealed class LandscapeChannelRecord : IKeyedRecord
{
    public int Id { get; set; }

    public int MapId { get; set; }

    public string Name { get; set; } = "Channel";

    public int Resolution { get; set; } = 64;

    public int BitDepth { get; set; } = 8;

    public int Components { get; set; } = 1;
}

/// <summary>EF Core row backing a <see cref="TerrainAttribute"/> in the Editor storage.</summary>
public sealed class TerrainAttributeRecord : IKeyedRecord
{
    public int Id { get; set; }

    public int MapId { get; set; }

    public string Key { get; set; } = "attribute";

    public string Name { get; set; } = "Attribute";

    public string Description { get; set; } = "";

    public int CellsPerChunkEdge { get; set; } = 1;

    public int Components { get; set; } = 1;

    public int ElementWidth { get; set; } = 32;

    public string ComponentNames { get; set; } = "";

    public int Kind { get; set; }

    public string CatalogName { get; set; } = "";

    // BIGINT: DefaultValue is an unsigned 32-bit value, which an INT column cannot hold in full.
    public long DefaultValue { get; set; }

    public bool Seeded { get; set; }
}

/// <summary>EF Core row backing a <see cref="TerrainAttributeValue"/> in the Editor storage.</summary>
public sealed class TerrainAttributeValueRecord : IKeyedRecord
{
    public int Id { get; set; }

    public int MapId { get; set; }

    public int AttributeId { get; set; }

    public long Value { get; set; }

    public string Name { get; set; } = "";
}

/// <summary>EF Core row backing a <see cref="LandscapeLayer"/> in the Editor storage.</summary>
public sealed class LandscapeLayerRecord : IKeyedRecord
{
    public int Id { get; set; }

    public int MapId { get; set; }

    public string Name { get; set; } = "Layer";

    public bool IsBase { get; set; }

    public int Priority { get; set; }

    public int DrawOrder { get; set; }
}

/// <summary>EF Core row backing a <see cref="LandscapeMaterial"/> in the Editor storage.</summary>
public sealed class LandscapeMaterialRecord : IKeyedRecord
{
    public int Id { get; set; }

    public int MapId { get; set; }

    public string Name { get; set; } = "Material";

    public string TexturePath { get; set; } = "";

    public string AlphaFunction { get; set; } = "";

    public string AlphaParameters { get; set; } = "";

    public string HeightFunction { get; set; } = "";

    public string HeightParameters { get; set; } = "";

    public string HoleFunction { get; set; } = "";

    public string HoleParameters { get; set; } = "";

    public string VertexColorFunction { get; set; } = "";

    public string VertexColorParameters { get; set; } = "";

    public string VertexLightFunction { get; set; } = "";

    public string VertexLightParameters { get; set; } = "";
}

/// <summary>
/// EF Core row holding one map's <see cref="LandscapeSettings"/>. Keyed by map id rather than a
/// generated key: a map has exactly one landscape, and no map means no row.
/// </summary>
public sealed class LandscapeSettingsRecord
{
    public int MapId { get; set; }

    public string ProfileName { get; set; } = "";

    public double ChunkWorldSize { get; set; }

    public int ChunkHeightResolution { get; set; }

    public int ChunkAlphaResolution { get; set; }

    public int ChunkHoleResolution { get; set; }

    public int HeightEncoding { get; set; }

    public double HeightOffset { get; set; }

    public double HeightScale { get; set; }

    public int AlphaBitDepth { get; set; }

    public bool AlphaNormalized { get; set; }

    public int OriginChunkX { get; set; }

    public int OriginChunkY { get; set; }

    public int ChunkLimit { get; set; }

    public int TextureLimit { get; set; }

    public int? FallbackMaterialId { get; set; }
}
