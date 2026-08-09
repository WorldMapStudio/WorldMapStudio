namespace WorldMapStudio;

/// <summary>EF Core row backing a <see cref="LandscapeChannel"/> in the Editor storage.</summary>
public sealed class LandscapeChannelRecord : IKeyedRecord
{
    public int Id { get; set; }

    public string Name { get; set; } = "Channel";

    public int Resolution { get; set; } = 64;

    public int BitDepth { get; set; } = 8;
}

/// <summary>EF Core row backing a <see cref="LandscapeLayer"/> in the Editor storage.</summary>
public sealed class LandscapeLayerRecord : IKeyedRecord
{
    public int Id { get; set; }

    public string Name { get; set; } = "Layer";

    public int Kind { get; set; }

    public bool IsBase { get; set; }

    public int Priority { get; set; }

    public int DrawOrder { get; set; }
}

/// <summary>EF Core row backing a <see cref="LandscapeMaterial"/> in the Editor storage.</summary>
public sealed class LandscapeMaterialRecord : IKeyedRecord
{
    public int Id { get; set; }

    public string Name { get; set; } = "Material";

    public string TexturePath { get; set; } = "";

    public string AlphaFunction { get; set; } = "";

    public string AlphaParameters { get; set; } = "";

    public string HeightFunction { get; set; } = "";

    public string HeightParameters { get; set; } = "";
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
