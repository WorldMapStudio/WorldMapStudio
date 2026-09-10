namespace WorldMapStudio;

/// <summary>
/// A material's reference to one <see cref="TerrainAttribute"/> from one attribute write, plus which
/// of its components a scalar write lands on. Serialized as <c>"key"</c> for a native binding or
/// <c>"key:swizzle"</c> otherwise, the same <c>name:suffix</c> convention
/// <see cref="LandscapeChannelBinding"/> uses and through the same <see cref="LandscapeSwizzleSyntax"/>
/// helper.
///
/// <see cref="LandscapeSwizzle.Luminance"/> is not offered: an attribute cell holds discrete values
/// (ids, flags) whose weighted average is meaningless, so a <c>:lum</c> suffix parses back as
/// <see cref="LandscapeSwizzle.Native"/>.
/// </summary>
public readonly record struct TerrainAttributeBinding(string Attribute, LandscapeSwizzle Swizzle)
{
    public static readonly TerrainAttributeBinding Empty = new("", LandscapeSwizzle.Native);

    public bool IsEmpty => Attribute.Length == 0;

    public static TerrainAttributeBinding Parse(string raw)
    {
        if (raw.Length == 0)
        {
            return Empty;
        }

        (string key, LandscapeSwizzle swizzle) = LandscapeSwizzleSyntax.Parse(raw, allowLuminance: false);
        return new TerrainAttributeBinding(key, swizzle);
    }

    public override string ToString() =>
        Swizzle == LandscapeSwizzle.Native ? Attribute : $"{Attribute}:{LandscapeSwizzleSyntax.SuffixOf(Swizzle)}";
}
