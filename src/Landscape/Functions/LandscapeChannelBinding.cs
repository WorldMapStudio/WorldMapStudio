using Godot;

namespace WorldMapStudio;

/// <summary>
/// Which component(s) of a bound <see cref="LandscapeChannel"/> a <see cref="LandscapeChannelBinding"/>
/// actually reads or writes. <see cref="Native"/> — whatever the channel declares — is the common case
/// and the only one that existed before a channel could carry more than one component.
/// </summary>
public enum LandscapeSwizzle
{
    /// <summary>Whatever the channel declares: a 1-component channel reads/writes as a scalar, a 3- or
    /// 4-component channel reads/writes as its own color.</summary>
    Native,

    R,
    G,
    B,
    A,

    /// <summary>Just the RGB components — alpha untouched on a write, ignored on a read.</summary>
    Rgb,

    /// <summary>Every component of an RGBA channel.</summary>
    Rgba,

    /// <summary>Perceptual luminance of the channel's RGB, as a scalar.</summary>
    Luminance,
}

/// <summary>
/// A material's reference to one <see cref="LandscapeChannel"/>, plus which of its components a
/// scalar or color read/write actually touches. Serialized as <c>"name"</c> for a native binding or
/// <c>"name:swizzle"</c> otherwise — <c>:</c> rather than <c>.</c> because a channel name is free user
/// text, and <see cref="LandscapeCatalog"/> rejects a channel name containing one.
///
/// This is what lets a function declared long before channels could carry more than one component —
/// every builtin alpha/height/hole function, which asks for a single scalar — keep working unchanged
/// against an RGB(A) channel: the function still asks for one float, the binding decides which
/// component of the channel that float comes from. A binding is per <em>use</em> of a channel, not per
/// channel, so the same RGB channel can feed three different scalar masks from three different
/// bindings.
/// </summary>
public readonly record struct LandscapeChannelBinding(string Channel, LandscapeSwizzle Swizzle)
{
    public static readonly LandscapeChannelBinding Empty = new("", LandscapeSwizzle.Native);

    public bool IsEmpty => Channel.Length == 0;

    /// <summary>Parses what <see cref="LandscapeParameterValues"/> stores for a channel parameter.
    /// Tolerant of an unrecognized suffix — it falls back to <see cref="LandscapeSwizzle.Native"/>
    /// rather than throwing, since a hand-edited or stale value should not crash a build.</summary>
    public static LandscapeChannelBinding Parse(string raw)
    {
        if (raw.Length == 0)
        {
            return Empty;
        }

        (string name, LandscapeSwizzle swizzle) = LandscapeSwizzleSyntax.Parse(raw);
        return new LandscapeChannelBinding(name, swizzle);
    }

    /// <summary>The suffix a non-native swizzle serializes as, or empty for native — shared by
    /// <see cref="ToString"/> and anything building the stored string incrementally (e.g. an editor
    /// combo that only changes the swizzle, keeping the channel name).</summary>
    public static string SuffixOf(LandscapeSwizzle swizzle) => LandscapeSwizzleSyntax.SuffixOf(swizzle);

    public override string ToString() =>
        Swizzle == LandscapeSwizzle.Native ? Channel : $"{Channel}:{SuffixOf(Swizzle)}";

    /// <summary>Reduces an already-widened color to the single number a swizzle asks for — shared by
    /// <see cref="LandscapeChannelPool.SampleScalar"/> (reading a channel) and a deformer that samples
    /// its own multi-component source (e.g. <see cref="ImageComponent"/> reading a painted image) and
    /// needs the same reduction before writing a scalar channel.</summary>
    public static float Extract(in Color color, LandscapeSwizzle swizzle) => swizzle switch
    {
        LandscapeSwizzle.R => color.R,
        LandscapeSwizzle.G => color.G,
        LandscapeSwizzle.B => color.B,
        LandscapeSwizzle.A => color.A,
        LandscapeSwizzle.Rgb or LandscapeSwizzle.Rgba or LandscapeSwizzle.Luminance =>
            (0.299f * color.R) + (0.587f * color.G) + (0.114f * color.B),
        _ => color.R,
    };
}
