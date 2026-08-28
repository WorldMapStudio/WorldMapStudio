using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

public enum ProceduralMeshParameterKind
{
    Float,
    Int,
    Bool,
    Texture,
    Color,
}

public sealed class ProceduralMeshParameter
{
    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public required ProceduralMeshParameterKind Kind { get; init; }

    public string Description { get; init; } = "";

    public float Min { get; init; } = float.MinValue;

    public float Max { get; init; } = float.MaxValue;

    public string Default { get; init; } = "";

    public static ProceduralMeshParameter Float(string name, string displayName, float @default, float min, float max, string description = "") =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            Kind = ProceduralMeshParameterKind.Float,
            Default = @default.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Min = min,
            Max = max,
            Description = description,
        };

    public static ProceduralMeshParameter Int(string name, string displayName, int @default, int min, int max, string description = "") =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            Kind = ProceduralMeshParameterKind.Int,
            Default = @default.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Min = min,
            Max = max,
            Description = description,
        };

    public static ProceduralMeshParameter Bool(string name, string displayName, bool @default, string description = "") =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            Kind = ProceduralMeshParameterKind.Bool,
            Default = @default ? "true" : "false",
            Description = description,
        };

    public static ProceduralMeshParameter Texture(string name, string displayName, string description = "") =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            Kind = ProceduralMeshParameterKind.Texture,
            Description = description,
        };

    public static ProceduralMeshParameter Color(string name, string displayName, Color @default, string description = "") =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            Kind = ProceduralMeshParameterKind.Color,
            Default = $"{@default.R.ToString(System.Globalization.CultureInfo.InvariantCulture)},{@default.G.ToString(System.Globalization.CultureInfo.InvariantCulture)},{@default.B.ToString(System.Globalization.CultureInfo.InvariantCulture)},{@default.A.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            Description = description,
        };

    public static IReadOnlyList<ProceduralMeshParameter> List(params ProceduralMeshParameter[] parameters) => parameters;
}
