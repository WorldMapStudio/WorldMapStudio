using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

public sealed partial class ProceduralMeshSystem : ISubsystemHost
{
    private readonly Dictionary<string, IProceduralMeshFunction> _byId = new(StringComparer.Ordinal);
    private readonly List<string> _warnings = [];

    public ProceduralMeshSystem(EditorContext context)
    {
        Context = context;
        InitializeSubsystems();
        DiscoverFrom(Subsystems.OfType<IProceduralMeshFunction>());
    }

    public EditorContext Context { get; }

    public IReadOnlyList<IProceduralMeshFunction> All { get; private set; } = [];

    public IReadOnlyList<string> Warnings => _warnings;

    public int Version { get; private set; }

    public IProceduralMeshFunction? Find(string id) =>
        id.Length > 0 && _byId.TryGetValue(id, out IProceduralMeshFunction? function) ? function : null;

    public ModelAsset Build(ProceduralMeshComponent component)
    {
        var builder = new ProceduralMeshOutputBuilder();
        if (Find(component.FunctionId) is not { } function)
        {
            return builder.Build(MeshModelFormat.FormatId);
        }

        string formatId = ResolveFormatId(component, function);
        IModelFormat? format = Context.ModelFormats.Find(formatId);
        IMeshMaterialType? materialType = format != null ? Context.MeshMaterials.Find(format.MaterialTypeId) : null;
        string materialTypeId = materialType?.Id ?? StandardMeshMaterial.TypeId;

        IReadOnlyDictionary<string, MeshMaterial> materials = ResolveMaterials(component, function, materialTypeId);

        function.Build(new ProceduralMeshBuildContext(
            component.Network,
            MeshParameterValues.Parse(component.Parameters),
            Context.Assets,
            format,
            materialType,
            slot => materials.TryGetValue(slot.Name, out MeshMaterial? material) ? material : new MeshMaterial(materialTypeId, new MeshParameterValues())),
            builder);
        return builder.Build(formatId);
    }

    /// <summary>Public overload for inspector/UI callers that only have the component, not its bound function.</summary>
    public string ResolveFormatId(ProceduralMeshComponent component) =>
        Find(component.FunctionId) is { } function ? ResolveFormatId(component, function) : MeshModelFormat.FormatId;

    /// <summary>
    /// The stored <see cref="ProceduralMeshComponent.FormatId"/> if the function still allows it,
    /// else the function's first supported format, else the plain authorable mesh format.
    /// </summary>
    private static string ResolveFormatId(ProceduralMeshComponent component, IProceduralMeshFunction function)
    {
        bool allowsAnyFormat = function.SupportedFormats.Count == 0;
        if (component.FormatId.Length > 0 && (allowsAnyFormat || function.SupportedFormats.Contains(component.FormatId)))
        {
            return component.FormatId;
        }

        return allowsAnyFormat ? MeshModelFormat.FormatId : function.SupportedFormats[0];
    }

    private Dictionary<string, MeshMaterial> ResolveMaterials(ProceduralMeshComponent component, IProceduralMeshFunction function, string materialTypeId)
    {
        var result = new Dictionary<string, MeshMaterial>(StringComparer.Ordinal);
        if (function.MaterialSlots.Count == 0)
        {
            return result;
        }

        ProceduralMeshMaterialBindings bindings = ProceduralMeshMaterialBindings.Parse(component.Materials);
        foreach (MeshMaterialSlot slot in function.MaterialSlots)
        {
            result[slot.Name] = ResolveSlot(bindings, slot, materialTypeId);
        }

        return result;
    }

    private MeshMaterial ResolveSlot(ProceduralMeshMaterialBindings bindings, MeshMaterialSlot slot, string materialTypeId)
    {
        if (bindings.GetPresetId(slot) is { } presetId)
        {
            MeshMaterialPreset? preset = Context.MeshMaterials.Presets.FirstOrDefault(p => p.RecordId == presetId);
            if (preset != null && preset.TypeId == materialTypeId)
            {
                return preset.ToMaterial();
            }
        }

        if (bindings.GetInlineValues(slot) is { } inline)
        {
            return new MeshMaterial(materialTypeId, inline);
        }

        return Context.MeshMaterials.Find(materialTypeId)?.Default ?? new MeshMaterial(materialTypeId, new MeshParameterValues());
    }

    public void DiscoverFrom(IEnumerable<IProceduralMeshFunction> functions)
    {
        _byId.Clear();
        _warnings.Clear();

        foreach (IProceduralMeshFunction function in functions)
        {
            Register(function);
        }

        All = _byId.Values.OrderBy(function => function.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        Version++;

        foreach (string warning in _warnings)
        {
            GD.PushWarning($"[ProceduralMesh] Function skipped - {warning}");
        }
    }

    private void Register(IProceduralMeshFunction function)
    {
        if (function.Id.Trim().Length == 0)
        {
            _warnings.Add($"{function.GetType().Name}: has an empty Id.");
            return;
        }

        if (_byId.TryGetValue(function.Id, out IProceduralMeshFunction? existing))
        {
            _warnings.Add($"{function.GetType().Name}: id '{function.Id}' is already used by {existing.GetType().Name}.");
            return;
        }

        if (DuplicateParameter(function) is { } duplicate)
        {
            _warnings.Add($"{function.GetType().Name}: declares parameter '{duplicate}' twice.");
            return;
        }

        _byId[function.Id] = function;
    }

    private static string? DuplicateParameter(IProceduralMeshFunction function)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (MeshParameter parameter in function.Parameters)
        {
            if (!seen.Add(parameter.Name))
            {
                return parameter.Name;
            }
        }

        return null;
    }
}
