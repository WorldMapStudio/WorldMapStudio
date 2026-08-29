using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

public enum MeshParameterKind
{
    Float,
    Int,
    Bool,
    Texture,
    Color,

    /// <summary>One of a fixed set of named string values, e.g. a blend mode or shader id.</summary>
    Choice,
}

/// <summary>One selectable value of a <see cref="MeshParameterKind.Choice"/> parameter.</summary>
public sealed record MeshParameterOption(string Value, string DisplayName, string Description = "");

/// <summary>
/// One parameter a <see cref="IMeshMaterialType"/> (or a procedural mesh function's material slot)
/// declares. Values are stored serialized in <see cref="MeshParameterValues"/> so a material's
/// authored data survives a type nothing currently provides, exactly like <c>LandscapeParameter</c>
/// does for landscape functions.
/// </summary>
public sealed class MeshParameter
{
    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public required MeshParameterKind Kind { get; init; }

    public string Description { get; init; } = "";

    public float Min { get; init; } = float.MinValue;

    public float Max { get; init; } = float.MaxValue;

    public string Default { get; init; } = "";

    /// <summary>Selectable values for a <see cref="MeshParameterKind.Choice"/> parameter. Empty otherwise.</summary>
    public IReadOnlyList<MeshParameterOption> Options { get; init; } = [];

    public static MeshParameter Float(string name, string displayName, float @default, float min, float max, string description = "") =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            Kind = MeshParameterKind.Float,
            Default = @default.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Min = min,
            Max = max,
            Description = description,
        };

    public static MeshParameter Int(string name, string displayName, int @default, int min, int max, string description = "") =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            Kind = MeshParameterKind.Int,
            Default = @default.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Min = min,
            Max = max,
            Description = description,
        };

    public static MeshParameter Bool(string name, string displayName, bool @default, string description = "") =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            Kind = MeshParameterKind.Bool,
            Default = @default ? "true" : "false",
            Description = description,
        };

    public static MeshParameter Texture(string name, string displayName, string description = "") =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            Kind = MeshParameterKind.Texture,
            Description = description,
        };

    public static MeshParameter Color(string name, string displayName, Color @default, string description = "") =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            Kind = MeshParameterKind.Color,
            Default = $"{@default.R.ToString(System.Globalization.CultureInfo.InvariantCulture)},{@default.G.ToString(System.Globalization.CultureInfo.InvariantCulture)},{@default.B.ToString(System.Globalization.CultureInfo.InvariantCulture)},{@default.A.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            Description = description,
        };

    public static MeshParameter Choice(string name, string displayName, IReadOnlyList<MeshParameterOption> options, string @default, string description = "") =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            Kind = MeshParameterKind.Choice,
            Default = @default,
            Options = options,
            Description = description,
        };

    public static IReadOnlyList<MeshParameter> List(params MeshParameter[] parameters) => parameters;
}
