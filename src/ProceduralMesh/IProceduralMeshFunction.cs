using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

public interface IProceduralMeshFunction : ISubsystem
{
    string Id { get; }

    string DisplayName { get; }

    string Description { get; }

    int Version { get; }

    bool AllowsMultipleGraphs => true;

    bool AllowsBranching => true;

    IReadOnlyList<ProceduralMeshParameter> Parameters { get; }

    void Build(in ProceduralMeshBuildContext context, ProceduralMeshOutputBuilder output);
}

public readonly struct ProceduralMeshBuildContext
{
    public ProceduralMeshBuildContext(
        ProceduralMeshNetwork network,
        ProceduralMeshParameterValues values,
        AssetSystem assets)
    {
        Network = network;
        Values = values;
        Assets = assets;
    }

    public ProceduralMeshNetwork Network { get; }

    public ProceduralMeshParameterValues Values { get; }

    public AssetSystem Assets { get; }

    public float Float(ProceduralMeshParameter parameter) => Values.GetFloat(parameter);

    public int Int(ProceduralMeshParameter parameter) => Values.GetInt(parameter);

    public bool Bool(ProceduralMeshParameter parameter) => Values.GetBool(parameter);

    public string Texture(ProceduralMeshParameter parameter) => Values.GetTexture(parameter);

    public Color Color(ProceduralMeshParameter parameter) => Values.GetColor(parameter);
}
