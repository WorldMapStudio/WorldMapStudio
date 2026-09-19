namespace WorldMapStudio;

/// <summary>
/// A named, saved <see cref="MeshMaterial"/> — what the Mesh Materials window authors and a
/// procedural mesh's material slot binds to. Catalog-backed like <see cref="LandscapeMaterial"/>:
/// edits go through the edit session so they undo and commit with everything else.
/// </summary>
public sealed class MeshMaterialPreset : CatalogEntity, IKeyedCatalogEntity
{
    [ScriptProperty(Mutable = true)]
    public string Name { get; set; } = "Material";

    /// <summary>Id of the <see cref="IMeshMaterialType"/> this preset's parameters belong to.</summary>
    [ScriptProperty(Mutable = true)]
    public string TypeId { get; set; } = StandardMeshMaterial.TypeId;

    /// <summary>Serialized <see cref="MeshParameterValues"/> for <see cref="TypeId"/>.</summary>
    [ScriptProperty(Mutable = true)]
    public string Parameters { get; set; } = "";

    /// <inheritdoc />
    public int? RecordId { get; set; }

    /// <inheritdoc />
    public bool IsSaved { get; set; }

    public override string DisplayName => Name;

    public MeshMaterial ToMaterial() => new(TypeId, MeshParameterValues.Parse(Parameters));
}
