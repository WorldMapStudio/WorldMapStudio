namespace WorldMapStudio;

/// <summary>Wavefront OBJ: no self-authoring, unrestricted placement, standard materials.</summary>
[Subsystem(nameof(ModelFormatSystem))]
public sealed class ObjModelFormat : IModelFormat
{
    public const string FormatId = "builtin.format.obj";

    public ObjModelFormat(ModelFormatSystem system)
    {
    }

    public string Id => FormatId;

    public string DisplayName => "OBJ";

    public string Extension => ".obj";

    public string MaterialTypeId => StandardMeshMaterial.TypeId;
}

/// <summary>glTF 2.0: no self-authoring, unrestricted placement, standard materials.</summary>
[Subsystem(nameof(ModelFormatSystem))]
public sealed class GltfModelFormat : IModelFormat
{
    public const string FormatId = "builtin.format.gltf";

    public GltfModelFormat(ModelFormatSystem system)
    {
    }

    public string Id => FormatId;

    public string DisplayName => "glTF";

    public string Extension => ".gltf";

    public string MaterialTypeId => StandardMeshMaterial.TypeId;
}

/// <summary>
/// Plain authorable mesh with no file-format pretensions — what a procedural mesh declares when it
/// isn't standing in for a specific imported format. Always available so a project with no
/// format-plugins loaded still has something a procedural function can author.
/// </summary>
[Subsystem(nameof(ModelFormatSystem))]
public sealed class MeshModelFormat : IModelFormat
{
    public const string FormatId = "builtin.format.mesh";

    public MeshModelFormat(ModelFormatSystem system)
    {
    }

    public string Id => FormatId;

    public string DisplayName => "Mesh";

    public string Extension => "";

    public string MaterialTypeId => StandardMeshMaterial.TypeId;

    public bool CanAuthor => true;
}
