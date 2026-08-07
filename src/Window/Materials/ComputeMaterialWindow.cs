using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

[Subsystem(nameof(WindowManager))]
public sealed class ComputeMaterialWindow : Window
{
    private const uint ShaderNameMaxLength = 128;
    private const uint MaterialNameMaxLength = 128;
    private const uint ShaderSourceMaxLength = 64 * 1024;
    private const uint ResourcePathMaxLength = 512;

    private readonly List<ParsedComputeShader> _shaders = [];
    private readonly List<ComputeMaterial> _materials = [];

    private string _shaderName = "Brightness Compute";
    private string _shaderSource = SampleComputeShader;
    private string _newMaterialName = "New Compute Material";
    private int _selectedShaderIndex = -1;
    private int _selectedMaterialIndex = -1;

    public ComputeMaterialWindow(WindowManager manager) : base("Compute Materials", defaultSize: new NVector2(980.0f, 680.0f))
    {
        ParseShader();
        CreateMaterial();
    }

    protected override void DrawContent()
    {
        NVector2 available = ImGui.GetContentRegionAvail();
        float leftWidth = Math.Max(360.0f, available.X * 0.45f);

        ImGui.BeginChild("ShaderPanel", new NVector2(leftWidth, 0.0f), true);
        DrawShaderPanel();
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("MaterialPanel", NVector2.Zero, true);
        DrawMaterialPanel();
        ImGui.EndChild();
    }

    private void DrawShaderPanel()
    {
        ImGui.Text("Shader");
        ImGui.PushItemWidth(-1.0f);
        ImGui.InputText("##ShaderName", ref _shaderName, ShaderNameMaxLength);
        ImGui.PopItemWidth();

        NVector2 sourceSize = new(-1.0f, Math.Max(220.0f, ImGui.GetContentRegionAvail().Y * 0.44f));
        ImGui.InputTextMultiline("##ShaderSource", ref _shaderSource, ShaderSourceMaxLength, sourceSize);

        if (ImGui.Button("Parse Shader"))
        {
            ParseShader();
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset Demo"))
        {
            _shaderName = "Brightness Compute";
            _shaderSource = SampleComputeShader;
            ParseShader();
        }

        ImGui.Separator();
        DrawShaderList();
        ImGui.Separator();
        DrawParsedShaderDetails(SelectedShader);
    }

    private void DrawShaderList()
    {
        ImGui.Text("Parsed Shaders");
        if (_shaders.Count == 0)
        {
            ImGui.TextDisabled("No shaders parsed yet.");
            return;
        }

        for (int i = 0; i < _shaders.Count; i++)
        {
            ParsedComputeShader shader = _shaders[i];
            string label = $"{shader.Name} ({shader.Parameters.Count})##shader{i}";
            if (ImGui.Selectable(label, _selectedShaderIndex == i))
            {
                _selectedShaderIndex = i;
                _shaderName = shader.Name;
                _shaderSource = shader.Source;
            }
        }
    }

    private void DrawParsedShaderDetails(ParsedComputeShader? shader)
    {
        if (shader == null)
        {
            return;
        }

        string computeState = shader.LooksLikeComputeShader ? "compute entry detected" : "compute entry not detected";
        ImGui.Text($"{shader.Parameters.Count} parameters, local size {shader.LocalSize.X:0}x{shader.LocalSize.Y:0}x{shader.LocalSize.Z:0}, {computeState}");

        foreach (string warning in shader.Warnings)
        {
            ImGui.TextColored(new NVector4(1.0f, 0.72f, 0.22f, 1.0f), warning);
        }

        if (shader.Parameters.Count == 0)
        {
            ImGui.TextDisabled("Paste a shader with uniforms, push constants, textures, images, or buffers.");
            return;
        }

        if (ImGui.BeginTable("ShaderParameters", 4, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("Type");
            ImGui.TableSetupColumn("Source");
            ImGui.TableSetupColumn("Binding");
            ImGui.TableHeadersRow();

            foreach (ShaderParameterDefinition parameter in shader.Parameters)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(parameter.DisplayName);
                ImGui.TableNextColumn();
                ImGui.Text(parameter.TypeName);
                ImGui.TableNextColumn();
                ImGui.Text(parameter.Source.ToString());
                ImGui.TableNextColumn();
                ImGui.Text(FormatBinding(parameter));
            }

            ImGui.EndTable();
        }
    }

    private void DrawMaterialPanel()
    {
        ImGui.Text("Materials");
        using (new Scoped(() => ImGui.PopItemWidth()))
        {
            ImGui.PushItemWidth(-1.0f);
            ImGui.InputText("##MaterialName", ref _newMaterialName, MaterialNameMaxLength);
        }

        bool canCreate = SelectedShader != null;
        if (!canCreate)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Create Material"))
        {
            CreateMaterial();
        }

        if (!canCreate)
        {
            ImGui.EndDisabled();
        }

        ImGui.Separator();
        DrawMaterialList();
        ImGui.Separator();
        DrawSelectedMaterial();
    }

    private void DrawMaterialList()
    {
        if (_materials.Count == 0)
        {
            ImGui.TextDisabled("No materials yet.");
            return;
        }

        for (int i = 0; i < _materials.Count; i++)
        {
            ComputeMaterial material = _materials[i];
            string label = $"{material.Name} -> {material.Shader.Name}##material{i}";
            if (ImGui.Selectable(label, _selectedMaterialIndex == i))
            {
                _selectedMaterialIndex = i;
            }
        }
    }

    private void DrawSelectedMaterial()
    {
        ComputeMaterial? material = SelectedMaterial;
        if (material == null)
        {
            ImGui.TextDisabled("Select or create a material.");
            return;
        }

        ImGui.Text(material.Name);
        ImGui.TextDisabled($"Shader: {material.Shader.Name}");

        if (material.Shader.Parameters.Count == 0)
        {
            ImGui.TextDisabled("This shader has no editable parameters.");
            return;
        }

        foreach (ShaderParameterDefinition definition in material.Shader.Parameters)
        {
            if (!material.Parameters.TryGetValue(definition.Name, out ComputeMaterialParameter? parameter))
            {
                parameter = ComputeMaterialParameter.FromDefinition(definition);
                material.Parameters[definition.Name] = parameter;
            }

            DrawParameterEditor(parameter);
        }
    }

    private void DrawParameterEditor(ComputeMaterialParameter parameter)
    {
        ShaderParameterDefinition definition = parameter.Definition;
        ImGui.PushID(definition.Name);
        ImGui.Separator();
        ImGui.Text(definition.DisplayName);
        ImGui.SameLine();
        ImGui.TextDisabled($"{definition.TypeName}, {definition.Source}, {FormatBinding(definition)}");

        switch (definition.Kind)
        {
            case ShaderParameterKind.Float:
                ImGui.DragFloat("Value", ref parameter.FloatValue, 0.01f);
                break;
            case ShaderParameterKind.Int:
                ImGui.DragInt("Value", ref parameter.IntValue);
                break;
            case ShaderParameterKind.UInt:
            {
                int value = (int)Math.Min(parameter.UIntValue, int.MaxValue);
                if (ImGui.DragInt("Value", ref value))
                {
                    parameter.UIntValue = (uint)Math.Max(0, value);
                }
                break;
            }
            case ShaderParameterKind.Bool:
                ImGui.Checkbox("Enabled", ref parameter.BoolValue);
                break;
            case ShaderParameterKind.Vector2:
                ImGui.DragFloat2("Value", ref parameter.Vector2Value, 0.01f);
                break;
            case ShaderParameterKind.Vector3:
                ImGui.DragFloat3("Value", ref parameter.Vector3Value, 0.01f);
                break;
            case ShaderParameterKind.Vector4:
                ImGui.DragFloat4("Value", ref parameter.Vector4Value, 0.01f);
                break;
            case ShaderParameterKind.Color3:
                ImGui.ColorEdit3("Color", ref parameter.Vector3Value);
                break;
            case ShaderParameterKind.Color4:
                ImGui.ColorEdit4("Color", ref parameter.Vector4Value);
                break;
            case ShaderParameterKind.Texture2D:
            case ShaderParameterKind.Texture3D:
            case ShaderParameterKind.TextureCube:
            case ShaderParameterKind.Image2D:
            case ShaderParameterKind.StorageBuffer:
            case ShaderParameterKind.UniformBuffer:
            case ShaderParameterKind.Unknown:
                DrawResourceSlot(parameter);
                break;
        }

        ImGui.PopID();
    }

    private void DrawResourceSlot(ComputeMaterialParameter parameter)
    {
        string label = parameter.Definition.Kind switch
        {
            ShaderParameterKind.Texture2D => "Texture2D Path",
            ShaderParameterKind.Texture3D => "Texture3D Path",
            ShaderParameterKind.TextureCube => "Cubemap Path",
            ShaderParameterKind.Image2D => "Storage Image Path",
            ShaderParameterKind.StorageBuffer => "Buffer Label",
            ShaderParameterKind.UniformBuffer => "Uniform Buffer Label",
            _ => "Value",
        };

        ImGui.PushItemWidth(-1.0f);
        ImGui.InputText(label, ref parameter.ResourcePath, ResourcePathMaxLength);
        ImGui.PopItemWidth();

        if (parameter.Definition.Kind is ShaderParameterKind.Texture2D or ShaderParameterKind.Texture3D or ShaderParameterKind.TextureCube or ShaderParameterKind.Image2D)
        {
            if (string.IsNullOrWhiteSpace(parameter.ResourcePath))
            {
                ImGui.TextDisabled("No resource assigned.");
            }
            else if (ResourceLoader.Exists(parameter.ResourcePath))
            {
                ImGui.TextColored(new NVector4(0.42f, 0.85f, 0.46f, 1.0f), "Resource found.");
            }
            else
            {
                ImGui.TextColored(new NVector4(1.0f, 0.52f, 0.42f, 1.0f), "Resource path not found.");
            }
        }
    }

    private void ParseShader()
    {
        ParsedComputeShader parsed = ComputeShaderParameterParser.Parse(_shaderName, _shaderSource);
        _shaders.Add(parsed);
        _selectedShaderIndex = _shaders.Count - 1;
    }

    private void CreateMaterial()
    {
        ParsedComputeShader? shader = SelectedShader;
        if (shader == null)
        {
            return;
        }

        string name = string.IsNullOrWhiteSpace(_newMaterialName)
            ? $"Material {_materials.Count + 1}"
            : _newMaterialName.Trim();

        _materials.Add(ComputeMaterial.Create(name, shader));
        _selectedMaterialIndex = _materials.Count - 1;
        _newMaterialName = NextMaterialName(name);
    }

    private ParsedComputeShader? SelectedShader
    {
        get
        {
            return _selectedShaderIndex >= 0 && _selectedShaderIndex < _shaders.Count
                ? _shaders[_selectedShaderIndex]
                : null;
        }
    }

    private ComputeMaterial? SelectedMaterial
    {
        get
        {
            return _selectedMaterialIndex >= 0 && _selectedMaterialIndex < _materials.Count
                ? _materials[_selectedMaterialIndex]
                : null;
        }
    }

    private static string FormatBinding(ShaderParameterDefinition parameter)
    {
        if (parameter.Set.HasValue && parameter.Binding.HasValue)
        {
            return $"set {parameter.Set.Value}, binding {parameter.Binding.Value}";
        }

        if (parameter.Binding.HasValue)
        {
            return $"binding {parameter.Binding.Value}";
        }

        return "-";
    }

    private static string NextMaterialName(string currentName)
    {
        Match match = System.Text.RegularExpressions.Regex.Match(currentName, @"^(?<base>.*?)(?:\s+(?<number>\d+))?$");
        string baseName = string.IsNullOrWhiteSpace(match.Groups["base"].Value) ? "Material" : match.Groups["base"].Value;
        int number = match.Groups["number"].Success && int.TryParse(match.Groups["number"].Value, out int parsed)
            ? parsed + 1
            : 2;
        return $"{baseName} {number}";
    }

    private const string SampleComputeShader = """
#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, rgba8) uniform image2D output_image;
layout(set = 0, binding = 1) uniform sampler2D source_texture;

layout(push_constant, std430) uniform Params {
    float exposure = 1.0;
    int iteration_count = 4;
    bool use_source = true;
    vec4 tint_color = vec4(1.0, 0.92, 0.78, 1.0);
} params;

layout(set = 0, binding = 2, std430) buffer HistogramBuffer {
    uint bins[];
} histogram;

void main() {
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    vec4 source_color = texelFetch(source_texture, pixel, 0);
    vec4 color = params.use_source ? source_color : vec4(1.0);
    color.rgb *= params.exposure * params.tint_color.rgb;
    imageStore(output_image, pixel, color);
}
""";
}
