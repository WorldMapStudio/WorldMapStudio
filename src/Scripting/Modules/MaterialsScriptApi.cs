using System;
using System.Linq;

namespace WorldMapStudio;

/// <summary>Mesh material types and saved presets, exposed to JS as <c>wms.materials</c>.</summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class MaterialsScriptApi : IScriptModule
{
    private readonly EditorContext _context;

    public string Name => "materials";

    public MaterialsScriptApi(ScriptingSystem system)
    {
        _context = system.Context;
    }

    private MeshMaterialSystem Materials => _context.MeshMaterials;

    /// <summary>The material types a preset can use, with their declared parameters.</summary>
    [ScriptFunction]
    public MaterialTypeDescriptor[] Types() => Materials.All.Select(type => new MaterialTypeDescriptor(type)).ToArray();

    /// <summary>The saved presets. A preset's <c>Name</c>, <c>TypeId</c> and <c>Parameters</c> can be
    /// written through its handle.</summary>
    [ScriptFunction]
    public ScriptEntityHandle[] Presets() => Materials.Presets.Select(ToHandle).ToArray();

    /// <summary>Creates a preset of a known type, undoably.</summary>
    [ScriptFunction]
    public ScriptEntityHandle CreatePreset(string name, string typeId)
    {
        if (Materials.Find(typeId) == null)
        {
            throw new InvalidOperationException($"No material type '{typeId}'.");
        }

        CreateCatalogEntityCommand command = Materials.BuildCreatePresetCommand(name, typeId, out MeshMaterialPreset preset);
        command.Apply();
        _context.EditSessions.Record(command);
        return ToHandle(preset);
    }

    /// <summary>Deletes a preset, undoably.</summary>
    [ScriptFunction]
    public void DeletePreset(ScriptEntityHandle handle)
    {
        if (handle.Resolve() is not MeshMaterialPreset preset)
        {
            throw new InvalidOperationException("That handle does not refer to a material preset.");
        }

        DeleteCatalogEntityCommand command = Materials.BuildDeletePresetCommand(preset);
        command.Apply();
        _context.EditSessions.Record(command);
    }

    /// <summary>What is wrong with the preset catalog, worst first.</summary>
    [ScriptFunction]
    public MaterialIssueDescriptor[] Validate() =>
        Materials.Validate().Select(issue => new MaterialIssueDescriptor(issue)).ToArray();

    private ScriptEntityHandle ToHandle(MeshMaterialPreset preset) =>
        new(_context.Scene, _context.Catalog, _context.EditSessions, preset);
}

/// <summary>An <see cref="IMeshMaterialType"/> as <c>wms.materials</c> reports it.</summary>
public sealed class MaterialTypeDescriptor
{
    public MaterialTypeDescriptor(IMeshMaterialType type)
    {
        Id = type.Id;
        DisplayName = type.DisplayName;
        Description = type.Description;
        Version = type.Version;
        Parameters = type.Parameters.Select(parameter => new MaterialParameterDescriptor(parameter)).ToArray();
    }

    [ScriptProperty] public string Id { get; }
    [ScriptProperty] public string DisplayName { get; }
    [ScriptProperty] public string Description { get; }
    [ScriptProperty] public int Version { get; }
    [ScriptProperty] public MaterialParameterDescriptor[] Parameters { get; }
}

/// <summary>A <see cref="MeshParameter"/> as <c>wms.materials</c> reports it.</summary>
public sealed class MaterialParameterDescriptor
{
    public MaterialParameterDescriptor(MeshParameter parameter)
    {
        Name = parameter.Name;
        DisplayName = parameter.DisplayName;
        Kind = parameter.Kind.ToString();
        Default = parameter.Default;
        Options = parameter.Options.Select(option => option.Value).ToArray();
    }

    [ScriptProperty] public string Name { get; }
    [ScriptProperty] public string DisplayName { get; }
    [ScriptProperty] public string Kind { get; }
    [ScriptProperty] public string Default { get; }

    /// <summary>The selectable values of a choice parameter, empty for any other kind.</summary>
    [ScriptProperty] public string[] Options { get; }
}

/// <summary>A <see cref="MeshMaterialIssue"/> as <c>wms.materials</c> reports it.</summary>
public sealed class MaterialIssueDescriptor
{
    public MaterialIssueDescriptor(MeshMaterialIssue issue)
    {
        Severity = issue.Severity.ToString();
        Message = issue.Message;
    }

    [ScriptProperty] public string Severity { get; }
    [ScriptProperty] public string Message { get; }
}
