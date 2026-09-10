namespace WorldMapStudio;

/// <summary>
/// The <c>"name:suffix"</c> serialization shared by <see cref="LandscapeChannelBinding"/> and
/// <see cref="TerrainAttributeBinding"/> — a target name plus which component(s) a scalar or color
/// read/write touches. Split out so the two bindings parse and print swizzles identically rather than
/// keeping two copies of the suffix table free to drift.
/// </summary>
public static class LandscapeSwizzleSyntax
{
    /// <summary>Splits a raw stored value into its name and its <see cref="LandscapeSwizzle"/>.
    /// Tolerant of an unrecognized suffix — it falls back to <see cref="LandscapeSwizzle.Native"/>
    /// rather than throwing, since a hand-edited or stale value should not crash a build. When
    /// <paramref name="allowLuminance"/> is false a <c>:lum</c> suffix is treated as unrecognized,
    /// for a consumer (attributes) where a weighted average of discrete ids is meaningless.</summary>
    public static (string Name, LandscapeSwizzle Swizzle) Parse(string raw, bool allowLuminance = true)
    {
        if (raw.Length == 0)
        {
            return ("", LandscapeSwizzle.Native);
        }

        int separator = raw.IndexOf(':');
        if (separator < 0)
        {
            return (raw, LandscapeSwizzle.Native);
        }

        string name = raw[..separator];
        LandscapeSwizzle swizzle = raw[(separator + 1)..].ToLowerInvariant() switch
        {
            "r" => LandscapeSwizzle.R,
            "g" => LandscapeSwizzle.G,
            "b" => LandscapeSwizzle.B,
            "a" => LandscapeSwizzle.A,
            "rgb" => LandscapeSwizzle.Rgb,
            "rgba" => LandscapeSwizzle.Rgba,
            "lum" when allowLuminance => LandscapeSwizzle.Luminance,
            _ => LandscapeSwizzle.Native,
        };

        return (name, swizzle);
    }

    /// <summary>The suffix a non-native swizzle serializes as, or empty for native.</summary>
    public static string SuffixOf(LandscapeSwizzle swizzle) => swizzle switch
    {
        LandscapeSwizzle.R => "r",
        LandscapeSwizzle.G => "g",
        LandscapeSwizzle.B => "b",
        LandscapeSwizzle.A => "a",
        LandscapeSwizzle.Rgb => "rgb",
        LandscapeSwizzle.Rgba => "rgba",
        LandscapeSwizzle.Luminance => "lum",
        _ => "",
    };
}
