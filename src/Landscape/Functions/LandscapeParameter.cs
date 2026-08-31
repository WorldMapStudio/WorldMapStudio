using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace WorldMapStudio;

/// <summary>What a landscape function parameter holds.</summary>
public enum LandscapeParameterKind
{
    Float,
    Int,
    Bool,

    /// <summary>Names one of the project's <see cref="LandscapeChannel"/>s. How a material tells a
    /// function which channel to read from or write to, since channels are user-defined data rather
    /// than something source code can name.</summary>
    Channel,

    /// <summary>An RGBA color, e.g. the tint a vertex color or vertex light function paints toward.</summary>
    Color,
}

/// <summary>How a function touches a channel it is bound to.</summary>
public enum LandscapeChannelAccess
{
    /// <summary>Not a channel parameter.</summary>
    None,

    /// <summary>Sampled during output evaluation, possibly across chunk borders.</summary>
    Read,

    /// <summary>Written during channel rasterization, strictly within the chunk being rasterized.</summary>
    Write,
}

/// <summary>
/// What shape of value a channel parameter expects — a hint for the binding UI's default and for
/// <see cref="LandscapeCatalog"/>'s "odd pairing" warning, never something that makes a binding
/// unrepresentable: a <see cref="LandscapeChannelBinding"/> swizzle can always bridge a scalar
/// parameter to a color channel or vice versa (see <see cref="LandscapeChannelPool.SampleScalar"/>/
/// <see cref="LandscapeChannelPool.SampleColor"/>), so a mismatch is only ever worth a warning.
/// </summary>
public enum LandscapeChannelFormat
{
    /// <summary>No preference — the parameter reads through <see cref="ILandscapeFunction"/>-specific
    /// code that is comfortable with either.</summary>
    Any,

    /// <summary>The function reads/writes a single value via <see cref="LandscapeEvalContext.SampleChannel"/>.</summary>
    Scalar,

    /// <summary>The function reads/writes a color via <see cref="LandscapeEvalContext.SampleChannelColor"/>.</summary>
    Color,
}

/// <summary>
/// One value a function takes, declared by the function and supplied by the material that uses it.
/// Parameters are what keep functions reusable: a "mask to alpha" function is written once and every
/// material points it at a different channel with a different falloff.
/// </summary>
public sealed class LandscapeParameter
{
    /// <summary>Stable key stored in the material's value bag. Renaming this orphans stored values.</summary>
    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public required LandscapeParameterKind Kind { get; init; }

    public string Description { get; init; } = "";

    /// <summary>Only meaningful for <see cref="LandscapeParameterKind.Channel"/>.</summary>
    public LandscapeChannelAccess Access { get; init; } = LandscapeChannelAccess.None;

    /// <summary>Only meaningful for <see cref="LandscapeParameterKind.Channel"/> — see
    /// <see cref="LandscapeChannelFormat"/>.</summary>
    public LandscapeChannelFormat ChannelFormat { get; init; } = LandscapeChannelFormat.Any;

    /// <summary>Only meaningful for <see cref="LandscapeParameterKind.Channel"/>: whether
    /// <see cref="LandscapeCatalog"/> should accept this parameter left unbound rather than reporting
    /// it as an error — for a channel a function falls back sensibly without, e.g. a mask that
    /// defaults to "always fully applies" when nothing narrows it.</summary>
    public bool Optional { get; init; }

    public float Min { get; init; } = float.MinValue;

    public float Max { get; init; } = float.MaxValue;

    /// <summary>Value used when the material has not set this parameter, in its serialized form.</summary>
    public string Default { get; init; } = "";

    public static LandscapeParameter Float(string name, string displayName, float @default, float min, float max, string description = "") =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            Kind = LandscapeParameterKind.Float,
            Default = @default.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Min = min,
            Max = max,
            Description = description,
        };

    public static LandscapeParameter Int(string name, string displayName, int @default, int min, int max, string description = "") =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            Kind = LandscapeParameterKind.Int,
            Default = @default.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Min = min,
            Max = max,
            Description = description,
        };

    public static LandscapeParameter Bool(string name, string displayName, bool @default, string description = "") =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            Kind = LandscapeParameterKind.Bool,
            Default = @default ? "true" : "false",
            Description = description,
        };

    public static LandscapeParameter Channel(
        string name,
        string displayName,
        LandscapeChannelAccess access,
        string description = "",
        LandscapeChannelFormat format = LandscapeChannelFormat.Any,
        bool optional = false) =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            Kind = LandscapeParameterKind.Channel,
            Access = access,
            Description = description,
            ChannelFormat = format,
            Optional = optional,
        };

    public static LandscapeParameter Color(string name, string displayName, Color @default, string description = "") =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            Kind = LandscapeParameterKind.Color,
            Default = string.Join(",",
                @default.R.ToString(CultureInfo.InvariantCulture),
                @default.G.ToString(CultureInfo.InvariantCulture),
                @default.B.ToString(CultureInfo.InvariantCulture),
                @default.A.ToString(CultureInfo.InvariantCulture)),
            Description = description,
        };

    /// <summary>Convenience for declaring a parameter list inline.</summary>
    public static IReadOnlyList<LandscapeParameter> List(params LandscapeParameter[] parameters) => parameters;
}
