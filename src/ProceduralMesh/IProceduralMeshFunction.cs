using System;
using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>A material slot a procedural function declares, filled from a preset or authored inline.</summary>
public sealed record MeshMaterialSlot(string Name, string DisplayName, string Description = "");

public interface IProceduralMeshFunction : ISubsystem
{
    string Id { get; }

    string DisplayName { get; }

    string Description { get; }

    int Version { get; }

    bool AllowsMultipleGraphs => true;

    bool AllowsBranching => true;

    IReadOnlyList<MeshParameter> Parameters { get; }

    /// <summary>Format ids this function can produce. Empty means any authorable format.</summary>
    IReadOnlyList<string> SupportedFormats => [];

    /// <summary>Materials the user binds for this function. A function that authors every material
    /// itself (e.g. a format-specific plugin function) declares none.</summary>
    IReadOnlyList<MeshMaterialSlot> MaterialSlots => [];

    void Build(in ProceduralMeshBuildContext context, ProceduralMeshOutputBuilder output);
}

public readonly struct ProceduralMeshBuildContext
{
    /// <summary>Convenience constructor for callers (mainly tests) that do not care about format/material resolution.</summary>
    public ProceduralMeshBuildContext(VertexNetwork network, MeshParameterValues values, AssetSystem assets)
        : this(network, values, assets, null, null, _ => StandardMeshMaterial.Describe())
    {
    }

    public ProceduralMeshBuildContext(
        VertexNetwork network,
        MeshParameterValues values,
        AssetSystem assets,
        IModelFormat? format,
        IMeshMaterialType? materialType,
        Func<MeshMaterialSlot, MeshMaterial> material)
    {
        Network = network;
        Values = values;
        Assets = assets;
        Format = format;
        MaterialType = materialType;
        _material = material;
    }

    private readonly Func<MeshMaterialSlot, MeshMaterial> _material;

    public VertexNetwork Network { get; }

    public MeshParameterValues Values { get; }

    public AssetSystem Assets { get; }

    /// <summary>The model format this build is authoring. Null if nothing loaded provides it.</summary>
    public IModelFormat? Format { get; }

    /// <summary>The material type <see cref="Format"/> renders through. Null if nothing loaded provides it.</summary>
    public IMeshMaterialType? MaterialType { get; }

    public float Float(MeshParameter parameter) => Values.GetFloat(parameter);

    public int Int(MeshParameter parameter) => Values.GetInt(parameter);

    public bool Bool(MeshParameter parameter) => Values.GetBool(parameter);

    public string Texture(MeshParameter parameter) => Values.GetTexture(parameter);

    public Color Color(MeshParameter parameter) => Values.GetColor(parameter);

    /// <summary>Resolves a declared material slot: bound preset, then inline values, then the material type's defaults.</summary>
    public MeshMaterial Material(MeshMaterialSlot slot) => _material(slot);
}
