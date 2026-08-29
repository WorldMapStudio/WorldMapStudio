using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// One material "language" a mesh surface can render through — the standard generic vocabulary, or a
/// format's own (e.g. WoW's M2/WMO blend and flag semantics). Registered under
/// <see cref="MeshMaterialSystem"/> via <c>[Subsystem(nameof(MeshMaterialSystem))]</c>, exactly like an
/// <see cref="IProceduralFunction"/> registers under <see cref="ProceduralSystem"/>.
/// </summary>
public interface IMeshMaterialType : ISubsystem
{
    string Id { get; }

    string DisplayName { get; }

    string Description { get; }

    int Version { get; }

    IReadOnlyList<MeshParameter> Parameters { get; }

    /// <summary>What an unbound material slot of this type should look like. Defaults to every
    /// parameter's own declared default; override where that reads badly (e.g. a thin procedural
    /// surface wants two-sided by default even though the type's own default is one-sided).</summary>
    MeshMaterial Default => new(Id, new MeshParameterValues());

    Material Build(in MeshMaterialBuildContext context);
}

public readonly struct MeshMaterialBuildContext
{
    public MeshMaterialBuildContext(AssetSystem assets, MeshParameterValues values)
    {
        Assets = assets;
        Values = values;
    }

    public AssetSystem Assets { get; }

    public MeshParameterValues Values { get; }

    public float Float(MeshParameter parameter) => Values.GetFloat(parameter);

    public int Int(MeshParameter parameter) => Values.GetInt(parameter);

    public bool Bool(MeshParameter parameter) => Values.GetBool(parameter);

    public string Texture(MeshParameter parameter) => Values.GetTexture(parameter);

    public Color Color(MeshParameter parameter) => Values.GetColor(parameter);

    public string Choice(MeshParameter parameter) => Values.GetChoice(parameter);
}
