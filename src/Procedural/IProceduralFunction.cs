using System;
using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>A material slot a procedural function declares, filled from a preset or authored inline.</summary>
public sealed record MeshMaterialSlot(string Name, string DisplayName, string Description = "");

/// <summary>
/// The network topology shapes a function knows how to build from, checked against an authored
/// network by <see cref="VertexNetwork.ValidateFor"/>. Bundled into one record rather than a growing
/// list of bool properties on <see cref="IProceduralFunction"/> so a new capability (like
/// <see cref="AllowsFaces"/>) does not have to touch every call site's signature.
/// </summary>
public sealed record NetworkCapabilities(
    bool AllowsMultipleGraphs = true,
    bool AllowsBranching = true,
    bool AllowsFaces = false)
{
    public static readonly NetworkCapabilities Default = new();
}

public interface IProceduralFunction : ISubsystem
{
    string Id { get; }

    string DisplayName { get; }

    string Description { get; }

    int Version { get; }

    NetworkCapabilities Capabilities => NetworkCapabilities.Default;

    IReadOnlyList<MeshParameter> Parameters { get; }

    /// <summary>
    /// Model outputs this function may fill, in the order they should be listed and instantiated.
    /// Empty for a function that only paints landscape channels (see <see cref="ProceduralOutputBuilder.AddStroke"/>).
    /// </summary>
    IReadOnlyList<ProceduralOutputSlot> Outputs => [];

    /// <summary>
    /// Whether this function's network is authored flat: vertices carry no meaningful height, so
    /// <see cref="NetworkEditTool"/> drops Y on every move/scale, rotates only about Y, and places new
    /// vertices on the terrain under the cursor instead of the entity's local plane. True for a road;
    /// false (unrestricted) for an ordinary mesh function.
    /// </summary>
    bool PlanarNetwork => false;

    /// <summary>How a placement bound to this function may rotate about itself. A function whose
    /// network is planar typically restricts this to <see cref="WorldMapStudio.SelfRotation.HeightOnly"/>.</summary>
    SelfRotation SelfRotation => SelfRotation.Full;

    /// <summary>How a placement bound to this function may be scaled.</summary>
    SelfScale SelfScale => SelfScale.PerAxis;

    /// <summary>Whether a new vertex, for a network that is not <see cref="PlanarNetwork"/>, should still
    /// snap onto the terrain surface when placed (keeping its real sampled height) rather than landing on
    /// the entity's local Y=0 plane — true for a fence, whose control points should start out sitting on
    /// the ground under the cursor rather than needing to be dragged down to it by hand.</summary>
    bool SnapToTerrainOnPlace => false;

    /// <summary>Whether the built output stays inside the convex hull of the network's own vertices and
    /// paints nothing, so bounds can be read off the network without a build.</summary>
    bool OutputWithinNetwork => false;

    /// <summary>Adjusts a vertex world position as it is placed or moved — e.g. snapping it onto a fixed
    /// lattice. Identity by default. Applied by <see cref="NetworkEditTool"/> after
    /// <see cref="PlanarNetwork"/>/<see cref="SnapToTerrainOnPlace"/> have resolved where the vertex
    /// would otherwise land.</summary>
    Vector3 SnapVertex(Vector3 worldPosition) => worldPosition;

    void Build(in ProceduralBuildContext context, ProceduralOutputBuilder output);
}

public readonly struct ProceduralBuildContext
{
    /// <summary>Convenience constructor for callers (mainly tests) that do not care about format/material resolution.</summary>
    public ProceduralBuildContext(VertexNetwork network, MeshParameterValues values, AssetSystem assets)
        : this(network, values, assets, _ => null, _ => null, (_, _) => StandardMeshMaterial.Describe())
    {
    }

    public ProceduralBuildContext(
        VertexNetwork network,
        MeshParameterValues values,
        AssetSystem assets,
        Func<ProceduralOutputSlot, IModelFormat?> format,
        Func<ProceduralOutputSlot, IMeshMaterialType?> materialType,
        Func<ProceduralOutputSlot, MeshMaterialSlot, MeshMaterial> material)
    {
        Network = network;
        Values = values;
        Assets = assets;
        _format = format;
        _materialType = materialType;
        _material = material;
    }

    private readonly Func<ProceduralOutputSlot, IModelFormat?> _format;
    private readonly Func<ProceduralOutputSlot, IMeshMaterialType?> _materialType;
    private readonly Func<ProceduralOutputSlot, MeshMaterialSlot, MeshMaterial> _material;

    public VertexNetwork Network { get; }

    public MeshParameterValues Values { get; }

    public AssetSystem Assets { get; }

    public float Float(MeshParameter parameter) => Values.GetFloat(parameter);

    public int Int(MeshParameter parameter) => Values.GetInt(parameter);

    public bool Bool(MeshParameter parameter) => Values.GetBool(parameter);

    public string Texture(MeshParameter parameter) => Values.GetTexture(parameter);

    public Color Color(MeshParameter parameter) => Values.GetColor(parameter);

    public string Channel(MeshParameter parameter) => Values.GetChannel(parameter);

    /// <summary>The model format <paramref name="slot"/> is authoring. Null if nothing loaded provides it.</summary>
    public IModelFormat? Format(ProceduralOutputSlot slot) => _format(slot);

    /// <summary>The material type <paramref name="slot"/> renders through. Null if nothing loaded provides it.</summary>
    public IMeshMaterialType? MaterialType(ProceduralOutputSlot slot) => _materialType(slot);

    /// <summary>Resolves one of <paramref name="slot"/>'s declared material slots: bound preset, then
    /// inline values, then the material type's defaults.</summary>
    public MeshMaterial Material(ProceduralOutputSlot slot, MeshMaterialSlot materialSlot) => _material(slot, materialSlot);
}
