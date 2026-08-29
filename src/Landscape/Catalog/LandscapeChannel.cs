namespace WorldMapStudio;

/// <summary>
/// A named scratch bitmap that entities draw into and functions read from — the only shared
/// vocabulary between an entity that knows how to draw a road and a function that knows how to turn
/// road-ness into alpha.
///
/// Channels are <em>transient</em>: what is persisted here is the declaration (how big, how precise),
/// never the pixels. The pixels are allocated from a pool per build and thrown away after.
/// </summary>
public sealed class LandscapeChannel : CatalogEntity, IKeyedCatalogEntity
{
    /// <summary>The map this channel belongs to.</summary>
    public MapId Map { get; set; } = new(0);

    public string Name { get; set; } = "Channel";

    /// <summary>Texels along a chunk edge. Independent of the map's alpha resolution: a mask feeding a
    /// wide falloff can afford to be coarser than the output it produces.</summary>
    public int Resolution { get; set; } = 64;

    /// <summary>Bits per texel: 8 for a mask, 16 where banding would show, 32 for distance fields.</summary>
    public int BitDepth { get; set; } = 8;

    /// <inheritdoc />
    public int? RecordId { get; set; }

    /// <inheritdoc />
    public bool IsSaved { get; set; }

    public override string DisplayName => Name;

    /// <summary>Bytes one chunk of this channel occupies, for the pool's budget.</summary>
    public int BytesPerChunk => Resolution * Resolution * (BitDepth / 8);
}
