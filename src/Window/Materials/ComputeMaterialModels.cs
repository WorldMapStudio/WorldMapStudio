using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace WorldMapStudio;

public enum ShaderParameterKind
{
    Float,
    Int,
    UInt,
    Bool,
    Vector2,
    Vector3,
    Vector4,
    Color3,
    Color4,
    Texture2D,
    Texture3D,
    TextureCube,
    Image2D,
    StorageBuffer,
    UniformBuffer,
    Unknown,
}

public enum ShaderParameterSource
{
    Uniform,
    PushConstant,
    UniformBlock,
    StorageBuffer,
    Texture,
    Image,
}

public sealed class ShaderParameterDefinition
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string TypeName { get; init; }
    public required ShaderParameterKind Kind { get; init; }
    public required ShaderParameterSource Source { get; init; }
    public int? Set { get; init; }
    public int? Binding { get; init; }
    public string? BlockName { get; init; }
    public string? InstanceName { get; init; }
    public string? DefaultValue { get; init; }
    public string? Hint { get; init; }
}

public sealed class ParsedComputeShader
{
    public required string Name { get; init; }
    public required string Source { get; init; }
    public required bool LooksLikeComputeShader { get; init; }
    public required Vector3 LocalSize { get; init; }
    public required List<ShaderParameterDefinition> Parameters { get; init; }
    public required List<string> Warnings { get; init; }
}

public sealed class ComputeMaterial
{
    public required string Name { get; init; }
    public required ParsedComputeShader Shader { get; init; }
    public Dictionary<string, ComputeMaterialParameter> Parameters { get; } = [];

    public static ComputeMaterial Create(string name, ParsedComputeShader shader)
    {
        var material = new ComputeMaterial { Name = name, Shader = shader };
        foreach (ShaderParameterDefinition definition in shader.Parameters)
        {
            material.Parameters[definition.Name] = ComputeMaterialParameter.FromDefinition(definition);
        }

        return material;
    }
}

public sealed class ComputeMaterialParameter
{
    public required ShaderParameterDefinition Definition { get; init; }
    public float FloatValue;
    public int IntValue;
    public uint UIntValue;
    public bool BoolValue;
    public Vector2 Vector2Value;
    public Vector3 Vector3Value;
    public Vector4 Vector4Value;
    public string ResourcePath = "";

    public static ComputeMaterialParameter FromDefinition(ShaderParameterDefinition definition)
    {
        var parameter = new ComputeMaterialParameter
        {
            Definition = definition,
            FloatValue = 0.0f,
            IntValue = 0,
            UIntValue = 0,
            BoolValue = false,
            Vector2Value = Vector2.Zero,
            Vector3Value = Vector3.Zero,
            Vector4Value = Vector4.Zero,
        };

        ShaderDefaultParser.ApplyDefaultValue(parameter, definition.DefaultValue);
        return parameter;
    }
}

public static partial class ComputeShaderParameterParser
{
    private static readonly Regex BlockDeclarationRegex = new(
        @"(?<layout>layout\s*\((?<layoutArgs>[^)]*)\)\s*)?(?<qualifiers>(?:(?:restrict|readonly|writeonly|coherent|volatile)\s+)*)?(?<storage>uniform|buffer)\s+(?<blockName>[A-Za-z_]\w*)\s*\{(?<body>.*?)\}\s*(?<instance>[A-Za-z_]\w*)?\s*;",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex UniformDeclarationRegex = new(
        @"(?<layout>layout\s*\((?<layoutArgs>[^)]*)\)\s*)?(?<qualifiers>(?:(?:restrict|readonly|writeonly|coherent|volatile)\s+)*)?uniform\s+(?<type>[A-Za-z_]\w*)\s+(?<name>[A-Za-z_]\w*)(?:\s*\[[^\]]*\])?(?:\s*:\s*(?<hint>[^=;]+))?(?:\s*=\s*(?<default>[^;]+))?\s*;",
        RegexOptions.Compiled);

    private static readonly Regex BlockMemberRegex = new(
        @"(?<type>[A-Za-z_]\w*)\s+(?<name>[A-Za-z_]\w*)(?:\s*\[[^\]]*\])?(?:\s*=\s*(?<default>[^;]+))?\s*;",
        RegexOptions.Compiled);

    private static readonly Regex LocalSizeRegex = new(
        @"layout\s*\((?<layoutArgs>[^)]*local_size[^)]*)\)\s*in\s*;",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public static ParsedComputeShader Parse(string name, string source)
    {
        string cleanSource = StripComments(source);
        var parameters = new List<ShaderParameterDefinition>();
        var warnings = new List<string>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in BlockDeclarationRegex.Matches(cleanSource))
        {
            string storage = match.Groups["storage"].Value;
            string blockName = match.Groups["blockName"].Value;
            string instanceName = match.Groups["instance"].Value;
            string layoutArgs = match.Groups["layoutArgs"].Value;
            (int? set, int? binding) = ParseSetAndBinding(layoutArgs);

            bool isPushConstant = layoutArgs.Contains("push_constant", StringComparison.OrdinalIgnoreCase);
            bool isStorageBuffer = storage == "buffer";
            if (isStorageBuffer)
            {
                AddUnique(parameters, seenNames, warnings, new ShaderParameterDefinition
                {
                    Name = string.IsNullOrWhiteSpace(instanceName) ? blockName : instanceName,
                    DisplayName = string.IsNullOrWhiteSpace(instanceName) ? blockName : instanceName,
                    TypeName = blockName,
                    Kind = ShaderParameterKind.StorageBuffer,
                    Source = ShaderParameterSource.StorageBuffer,
                    Set = set,
                    Binding = binding,
                    BlockName = blockName,
                    InstanceName = instanceName,
                });
                continue;
            }

            foreach (Match member in BlockMemberRegex.Matches(match.Groups["body"].Value))
            {
                string typeName = member.Groups["type"].Value;
                string memberName = member.Groups["name"].Value;
                string fullName = string.IsNullOrWhiteSpace(instanceName) ? memberName : $"{instanceName}.{memberName}";
                string defaultValue = member.Groups["default"].Success ? member.Groups["default"].Value.Trim() : "";

                AddUnique(parameters, seenNames, warnings, new ShaderParameterDefinition
                {
                    Name = fullName,
                    DisplayName = memberName,
                    TypeName = typeName,
                    Kind = MapKind(typeName, null, memberName),
                    Source = isPushConstant ? ShaderParameterSource.PushConstant : ShaderParameterSource.UniformBlock,
                    Set = set,
                    Binding = binding,
                    BlockName = blockName,
                    InstanceName = instanceName,
                    DefaultValue = defaultValue,
                });
            }
        }

        string withoutBlocks = BlockDeclarationRegex.Replace(cleanSource, "");
        foreach (Match match in UniformDeclarationRegex.Matches(withoutBlocks))
        {
            string typeName = match.Groups["type"].Value;
            string parameterName = match.Groups["name"].Value;
            string hint = match.Groups["hint"].Success ? match.Groups["hint"].Value.Trim() : "";
            string defaultValue = match.Groups["default"].Success ? match.Groups["default"].Value.Trim() : "";
            string layoutArgs = match.Groups["layoutArgs"].Value;
            (int? set, int? binding) = ParseSetAndBinding(layoutArgs);
            ShaderParameterKind kind = MapKind(typeName, hint, parameterName);

            AddUnique(parameters, seenNames, warnings, new ShaderParameterDefinition
            {
                Name = parameterName,
                DisplayName = parameterName,
                TypeName = typeName,
                Kind = kind,
                Source = kind switch
                {
                    ShaderParameterKind.Texture2D or ShaderParameterKind.Texture3D or ShaderParameterKind.TextureCube => ShaderParameterSource.Texture,
                    ShaderParameterKind.Image2D => ShaderParameterSource.Image,
                    _ => ShaderParameterSource.Uniform,
                },
                Set = set,
                Binding = binding,
                DefaultValue = defaultValue,
                Hint = hint,
            });
        }

        Vector3 localSize = ParseLocalSize(cleanSource);
        bool looksLikeCompute = cleanSource.Contains("#[compute]", StringComparison.OrdinalIgnoreCase) ||
                                LocalSizeRegex.IsMatch(cleanSource) ||
                                cleanSource.Contains("void main()", StringComparison.OrdinalIgnoreCase) &&
                                cleanSource.Contains("gl_GlobalInvocationID", StringComparison.Ordinal);

        if (!looksLikeCompute)
        {
            warnings.Add("No compute entry point was detected. Expected #[compute], layout(local_size_*) in;, or gl_GlobalInvocationID usage.");
        }

        return new ParsedComputeShader
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Untitled Shader" : name.Trim(),
            Source = source,
            LooksLikeComputeShader = looksLikeCompute,
            LocalSize = localSize,
            Parameters = parameters,
            Warnings = warnings,
        };
    }

    private static void AddUnique(
        List<ShaderParameterDefinition> parameters,
        HashSet<string> seenNames,
        List<string> warnings,
        ShaderParameterDefinition definition)
    {
        if (!seenNames.Add(definition.Name))
        {
            warnings.Add($"Skipped duplicate parameter '{definition.Name}'.");
            return;
        }

        parameters.Add(definition);
    }

    private static string StripComments(string source)
    {
        string withoutBlockComments = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(withoutBlockComments, @"//.*?$", "", RegexOptions.Multiline);
    }

    private static (int? Set, int? Binding) ParseSetAndBinding(string layoutArgs)
    {
        return (ReadLayoutInt(layoutArgs, "set"), ReadLayoutInt(layoutArgs, "binding"));
    }

    private static int? ReadLayoutInt(string layoutArgs, string name)
    {
        Match match = Regex.Match(layoutArgs, $@"(?:^|,)\s*{Regex.Escape(name)}\s*=\s*(?<value>\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }

    private static Vector3 ParseLocalSize(string source)
    {
        Match match = LocalSizeRegex.Match(source);
        if (!match.Success)
        {
            return new Vector3(1.0f, 1.0f, 1.0f);
        }

        string layoutArgs = match.Groups["layoutArgs"].Value;
        return new Vector3(
            ReadLayoutInt(layoutArgs, "local_size_x") ?? 1,
            ReadLayoutInt(layoutArgs, "local_size_y") ?? 1,
            ReadLayoutInt(layoutArgs, "local_size_z") ?? 1);
    }

    private static ShaderParameterKind MapKind(string typeName, string? hint, string parameterName)
    {
        string normalized = typeName.Trim();
        string hintText = hint ?? "";
        bool looksLikeColor = hintText.Contains("source_color", StringComparison.OrdinalIgnoreCase) ||
                              parameterName.Contains("color", StringComparison.OrdinalIgnoreCase) ||
                              parameterName.Contains("tint", StringComparison.OrdinalIgnoreCase);

        return normalized switch
        {
            "float" or "double" => ShaderParameterKind.Float,
            "int" => ShaderParameterKind.Int,
            "uint" => ShaderParameterKind.UInt,
            "bool" => ShaderParameterKind.Bool,
            "vec2" or "ivec2" or "uvec2" => ShaderParameterKind.Vector2,
            "vec3" or "ivec3" or "uvec3" => looksLikeColor ? ShaderParameterKind.Color3 : ShaderParameterKind.Vector3,
            "vec4" or "ivec4" or "uvec4" => looksLikeColor ? ShaderParameterKind.Color4 : ShaderParameterKind.Vector4,
            "sampler2D" or "texture2D" => ShaderParameterKind.Texture2D,
            "sampler3D" or "texture3D" => ShaderParameterKind.Texture3D,
            "samplerCube" or "textureCube" => ShaderParameterKind.TextureCube,
            "image2D" => ShaderParameterKind.Image2D,
            _ => ShaderParameterKind.Unknown,
        };
    }
}

public static class ShaderDefaultParser
{
    private static readonly Regex NumberRegex = new(
        @"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?",
        RegexOptions.Compiled);

    public static void ApplyDefaultValue(ComputeMaterialParameter parameter, string? defaultValue)
    {
        if (string.IsNullOrWhiteSpace(defaultValue))
        {
            return;
        }

        List<float> numbers = [];
        foreach (Match match in NumberRegex.Matches(defaultValue))
        {
            if (float.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                numbers.Add(value);
            }
        }

        switch (parameter.Definition.Kind)
        {
            case ShaderParameterKind.Float:
                parameter.FloatValue = numbers.Count > 0 ? numbers[0] : 0.0f;
                break;
            case ShaderParameterKind.Int:
                parameter.IntValue = numbers.Count > 0 ? (int)numbers[0] : 0;
                break;
            case ShaderParameterKind.UInt:
                parameter.UIntValue = numbers.Count > 0 ? (uint)Math.Max(0.0f, numbers[0]) : 0;
                break;
            case ShaderParameterKind.Bool:
                parameter.BoolValue = defaultValue.Contains("true", StringComparison.OrdinalIgnoreCase) ||
                                      (numbers.Count > 0 && Math.Abs(numbers[0]) > float.Epsilon);
                break;
            case ShaderParameterKind.Vector2:
                parameter.Vector2Value = new Vector2(Get(numbers, 0), Get(numbers, 1));
                break;
            case ShaderParameterKind.Vector3:
            case ShaderParameterKind.Color3:
                parameter.Vector3Value = new Vector3(Get(numbers, 0), Get(numbers, 1), Get(numbers, 2));
                break;
            case ShaderParameterKind.Vector4:
            case ShaderParameterKind.Color4:
                parameter.Vector4Value = new Vector4(Get(numbers, 0), Get(numbers, 1), Get(numbers, 2), Get(numbers, 3, 1.0f));
                break;
        }
    }

    private static float Get(List<float> values, int index, float fallback = 0.0f)
    {
        return index < values.Count ? values[index] : fallback;
    }
}
