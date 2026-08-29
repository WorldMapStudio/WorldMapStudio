using Godot;
using ImGuiNET;

namespace WorldMapStudio;

[Subsystem(nameof(SceneComponentRegistry))]
public sealed class ProceduralMeshComponentType : ISceneComponentType
{
    private readonly ProceduralMeshSystem _system;
    private readonly TextureAssetPicker _texturePicker;
    private string? _parameterBefore;

    public float Priority => 5.0f;

    public ProceduralMeshComponentType(SceneComponentRegistry registry)
    {
        _system = registry.Context.ProceduralMeshes;
        _texturePicker = new TextureAssetPicker(registry.Context.Assets);
    }

    public string TypeId => "procedural-mesh";

    public string DisplayName => "Procedural Mesh";

    public SceneComponent Create()
    {
        var component = new ProceduralMeshComponent(_system);
        int a = component.Network.AddVertex(new Vector3(-1.0f, 0.0f, 0.0f));
        int b = component.Network.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));
        component.Network.AddEdge(a, b);
        return component;
    }

    public void DrawInspector(InspectorContext context, SceneComponent component)
    {
        var procedural = (ProceduralMeshComponent)component;

        IProceduralMeshFunction? bound = _system.Find(procedural.FunctionId);
        string label = bound?.DisplayName ?? (procedural.FunctionId.Length == 0 ? "(none)" : $"{procedural.FunctionId} (missing)");

        if (ImGui.BeginCombo("Function", label))
        {
            foreach (IProceduralMeshFunction function in _system.All)
            {
                if (ImGui.Selectable($"{function.DisplayName}##{function.Id}", function.Id == procedural.FunctionId))
                {
                    ComponentFieldRecorder.Record(context, procedural, "function", procedural.FunctionId, function.Id, value => procedural.FunctionId = value);
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(function.Description);
                }
            }

            ImGui.EndCombo();
        }

        if (bound == null)
        {
            if (procedural.FunctionId.Length > 0)
            {
                ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.45f, 0.4f, 1.0f), $"No loaded function provides '{procedural.FunctionId}'.");
            }
        }
        else
        {
            string graphMode = bound.AllowsMultipleGraphs ? "multiple graphs" : "single graph";
            string branchMode = bound.AllowsBranching ? "branching" : "linear";
            ImGui.TextDisabled($"v{bound.Version}, {graphMode}, {branchMode}");
            foreach (string problem in procedural.Network.ValidateFor(bound.DisplayName, bound.AllowsMultipleGraphs, bound.AllowsBranching))
            {
                ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.72f, 0.22f, 1.0f), problem);
            }

            DrawParameters(context, procedural, bound);
        }

        ImGui.Separator();
        ImGui.TextDisabled($"{procedural.Network.Vertices.Count} vertices, {procedural.Network.Edges.Count} edges");
    }

    public void DrawModals() => _texturePicker.Draw();

    private void DrawParameters(InspectorContext context, ProceduralMeshComponent component, IProceduralMeshFunction function)
    {
        string serialized = component.Parameters;
        ProceduralMeshParameterValues values = ProceduralMeshParameterValues.Parse(serialized);

        foreach (ProceduralMeshParameter parameter in function.Parameters)
        {
            ImGui.PushID(parameter.Name);

            switch (parameter.Kind)
            {
                case ProceduralMeshParameterKind.Float:
                {
                    float value = values.GetFloat(parameter);
                    if (ImGui.DragFloat(parameter.DisplayName, ref value, 0.01f, parameter.Min, parameter.Max))
                    {
                        values.Set(parameter, value);
                    }

                    TrackParameter(context, component, function, values);
                    break;
                }
                case ProceduralMeshParameterKind.Int:
                {
                    int value = values.GetInt(parameter);
                    if (ImGui.DragInt(parameter.DisplayName, ref value, 1.0f, (int)parameter.Min, (int)parameter.Max))
                    {
                        values.Set(parameter, value);
                    }

                    TrackParameter(context, component, function, values);
                    break;
                }
                case ProceduralMeshParameterKind.Bool:
                {
                    bool value = values.GetBool(parameter);
                    if (ImGui.Checkbox(parameter.DisplayName, ref value))
                    {
                        values.Set(parameter, value);
                        ComponentFieldRecorder.Record(context, component, parameter.DisplayName, component.Parameters, values.Serialize(), v => component.Parameters = v);
                    }

                    break;
                }
                case ProceduralMeshParameterKind.Texture:
                {
                    string texture = values.GetTexture(parameter);
                    DrawAssetPath(parameter.DisplayName, texture);
                    ImGui.SameLine();
                    if (ImGui.Button($"Browse##{parameter.Name}"))
                    {
                        _texturePicker.Browse(texture, selected =>
                        {
                            ProceduralMeshParameterValues changed = ProceduralMeshParameterValues.Parse(component.Parameters);
                            changed.Set(parameter, selected);
                            ComponentFieldRecorder.Record(context, component, parameter.DisplayName, component.Parameters, changed.Serialize(), v => component.Parameters = v);
                        });
                    }

                    if (texture.Length > 0)
                    {
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"Clear##{parameter.Name}"))
                        {
                            values.Set(parameter, "");
                            ComponentFieldRecorder.Record(context, component, parameter.DisplayName, component.Parameters, values.Serialize(), v => component.Parameters = v);
                        }
                    }

                    break;
                }
                case ProceduralMeshParameterKind.Color:
                {
                    Color color = values.GetColor(parameter);
                    var value = new System.Numerics.Vector4(color.R, color.G, color.B, color.A);
                    if (ImGui.ColorEdit4(parameter.DisplayName, ref value))
                    {
                        values.Set(parameter, new Color(value.X, value.Y, value.Z, value.W));
                    }

                    TrackParameter(context, component, function, values);
                    break;
                }
            }

            if (parameter.Description.Length > 0 && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(parameter.Description);
            }

            ImGui.PopID();
        }
    }

    private void TrackParameter(
        InspectorContext context,
        ProceduralMeshComponent component,
        IProceduralMeshFunction function,
        ProceduralMeshParameterValues values)
    {
        if (ImGui.IsItemActivated())
        {
            _parameterBefore = component.Parameters;
        }

        if (!ImGui.IsItemDeactivatedAfterEdit() || _parameterBefore == null)
        {
            return;
        }

        string before = _parameterBefore;
        _parameterBefore = null;
        string after = values.Serialize();
        if (before != after)
        {
            ComponentFieldRecorder.Record(context, component, $"{function.DisplayName} parameters", before, after, value => component.Parameters = value);
        }
    }

    private static void DrawAssetPath(string label, string path)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{label}:");
        ImGui.SameLine();
        ImGui.TextDisabled(path.Length == 0 ? "(none)" : path);
    }
}
