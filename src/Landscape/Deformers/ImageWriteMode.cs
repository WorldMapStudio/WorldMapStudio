namespace WorldMapStudio;

/// <summary>How an <see cref="ImageComponent"/> combines each sampled value with what a landscape
/// channel texel already holds, on the scalar write path.</summary>
public enum ImageWriteMode
{
    /// <summary>Keep the larger of the existing texel and the sampled value, and skip a non-positive
    /// sample entirely. This is the coverage-combine behaviour a mask wants — several placements
    /// feeding one channel accumulate rather than overwrite — and it is the default.</summary>
    Max,

    /// <summary>Overwrite the texel with the sampled value, whatever its sign. What a buffer that
    /// carries a signed quantity directly (a heightfield, an offset field) wants: a <see cref="Max"/>
    /// against a zero-initialised channel would discard every negative sample, and skipping
    /// non-positive samples would punch holes wherever the value is legitimately zero.</summary>
    Replace,
}
